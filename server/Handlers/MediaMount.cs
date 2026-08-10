using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection;
using System.Xml;
using NMaier.SimpleDlna.Server.Metadata;
using NMaier.SimpleDlna.Server.Properties;
using NMaier.SimpleDlna.Utilities;

namespace NMaier.SimpleDlna.Server
{
  internal sealed partial class MediaMount
    : Logging, IMediaServer, IPrefixHandler, IDisposable
  {
    // Service ids used for the eventing endpoints. Each service needs its own
    // eventSubURL, otherwise a SUBSCRIBE cannot be attributed to a service.
    private const string CONTENT_DIRECTORY = "cds";

    private const string CONNECTION_MANAGER = "cms";

    private const string MEDIA_RECEIVER_REGISTRAR = "mrr";

    private static uint mount;

    private readonly Dictionary<IPAddress, Guid> guidsForAddresses =
      new Dictionary<IPAddress, Guid>();

    private readonly IMediaServer server;

    private readonly EventSubscriptions events = new EventSubscriptions();

    private uint systemID = 1;

    public MediaMount(IMediaServer aServer)
    {
      server = aServer;
      Prefix = $"/mm-{++mount}/";
      var vms = server as IVolatileMediaServer;
      if (vms != null) {
        vms.Changed += ChangedServer;
      }
    }

    public void Dispose()
    {
      var vms = server as IVolatileMediaServer;
      if (vms != null) {
        vms.Changed -= ChangedServer;
      }
      events.Dispose();
    }

    public string DescriptorURI => $"{Prefix}description.xml";

    public IHttpAuthorizationMethod Authorizer => server.Authorizer;

    public string FriendlyName => server.FriendlyName;

    public Guid UUID => server.UUID;

    public IMediaItem GetItem(string id)
    {
      return server.GetItem(id);
    }

    public string Prefix { get; }

    public IResponse HandleRequest(IRequest request)
    {
      if (Authorizer != null &&
          !IPAddress.IsLoopback(request.RemoteEndpoint.Address) &&
          !Authorizer.Authorize(
            request.Headers,
            request.RemoteEndpoint,
            IP.GetMAC(request.RemoteEndpoint.Address)
            )) {
        throw new HttpStatusException(HttpCode.Denied);
      }

      var path = request.Path.Substring(Prefix.Length);
      Debug(path);
      if (path == "description.xml") {
        return new StringResponse(
          HttpCode.Ok,
          "text/xml",
          GenerateDescriptor(request.LocalEndPoint.Address)
          );
      }
      if (path == "contentDirectory.xml") {
        return new ResourceResponse(
          HttpCode.Ok,
          "text/xml",
          "contentdirectory"
          );
      }
      if (path == "connectionManager.xml") {
        return new ResourceResponse(
          HttpCode.Ok,
          "text/xml",
          "connectionmanager"
          );
      }
      if (path == "MSMediaReceiverRegistrar.xml") {
        return new ResourceResponse(
          HttpCode.Ok,
          "text/xml",
          "MSMediaReceiverRegistrar"
          );
      }
      if (path == "control") {
        return ProcessSoapRequest(request);
      }
      if (path.StartsWith("file/", StringComparison.Ordinal)) {
        var id = path.Split('/')[1];
        InfoFormat("Serving file {0}", id);
        var item = GetItem(id) as IMediaResource;
        return new ItemResponse(Prefix, request, item);
      }
      if (path.StartsWith("cover/", StringComparison.Ordinal)) {
        var id = path.Split('/')[1];
        InfoFormat("Serving cover {0}", id);
        var item = GetItem(id) as IMediaCover;
        if (item == null) {
          throw new HttpStatusException(HttpCode.NotFound);
        }
        return new ItemResponse(Prefix, request, item.Cover, "Interactive");
      }
      if (path.StartsWith("subtitle/", StringComparison.Ordinal)) {
        var id = path.Split('/')[1];
        InfoFormat("Serving subtitle {0}", id);
        var item = GetItem(id) as IMetaVideoItem;
        if (item == null) {
          throw new HttpStatusException(HttpCode.NotFound);
        }
        return new ItemResponse(Prefix, request, item.Subtitle, "Background");
      }

      if (string.IsNullOrEmpty(path) || path == "index.html") {
        return new Redirect(request, Prefix + "index/0");
      }
      if (path.StartsWith("index/", StringComparison.Ordinal)) {
        var id = path.Substring("index/".Length);
        var item = GetItem(id);
        return ProcessHtmlRequest(item);
      }
      if (path.StartsWith("events", StringComparison.Ordinal)) {
        return HandleEventRequest(request, path);
      }
      WarnFormat("Did not understand {0} {1}", request.Method, path);
      throw new HttpStatusException(HttpCode.NotFound);
    }

    private void ChangedServer(object sender, EventArgs e)
    {
      soapCache.Clear();
      InfoFormat("Rescanned mount {0}", UUID);
      systemID++;
      // Tell every subscribed control point that the library moved on. Bumping
      // SystemUpdateID alone only helps clients that poll; ContainerUpdateIDs
      // is what makes a TV re-read the container it is showing.
      events.Notify(CONTENT_DIRECTORY, ContentDirectoryState());
    }

    /// <summary>The evented ContentDirectory state variables.</summary>
    private IEnumerable<KeyValuePair<string, string>> ContentDirectoryState()
    {
      return new[]
      {
        new KeyValuePair<string, string>(
          "SystemUpdateID", systemID.ToString()),
        new KeyValuePair<string, string>(
          "ContainerUpdateIDs", $"{Identifiers.GENERAL_ROOT},{systemID}"),
        new KeyValuePair<string, string>("TransferIDs", string.Empty)
      };
    }

    private IEnumerable<KeyValuePair<string, string>> StateFor(string service)
    {
      switch (service) {
      case CONTENT_DIRECTORY:
        return ContentDirectoryState();
      case CONNECTION_MANAGER:
        return new[]
        {
          new KeyValuePair<string, string>(
            "SourceProtocolInfo", DlnaMaps.ProtocolInfo),
          new KeyValuePair<string, string>("SinkProtocolInfo", string.Empty),
          new KeyValuePair<string, string>("CurrentConnectionIDs", "0")
        };
      default:
        return new[]
        {
          new KeyValuePair<string, string>("AuthorizationGrantedUpdateID", "0"),
          new KeyValuePair<string, string>("AuthorizationDeniedUpdateID", "0"),
          new KeyValuePair<string, string>("ValidationSucceededUpdateID", "0"),
          new KeyValuePair<string, string>("ValidationRevokedUpdateID", "0")
        };
      }
    }

    /// <summary>
    ///   The headers indexer throws when a key is absent, and every GENA header
    ///   here is optional depending on whether this is a new subscription, a
    ///   renewal or an unsubscribe.
    /// </summary>
    private static string Header(IRequest request, string key)
    {
      return request.Headers.ContainsKey(key) ? request.Headers[key] : null;
    }

    private IResponse HandleEventRequest(IRequest request, string path)
    {
      var service = path.Length > "events/".Length
        ? path.Substring("events/".Length)
        : CONTENT_DIRECTORY;

      if (request.Method == "SUBSCRIBE") {
        var sid = Header(request, "sid");
        var sub = !string.IsNullOrEmpty(sid)
          ? events.Renew(sid, Header(request, "timeout"))
          : events.Subscribe(
            service, Header(request, "callback"), Header(request, "timeout"));
        if (sub == null) {
          // Either a renewal for an SID we never issued (the spec wants 412
          // so the client re-subscribes) or a SUBSCRIBE without a CALLBACK.
          throw new HttpStatusException(HttpCode.PreconditionFailed);
        }
        var res = new StringResponse(HttpCode.Ok, string.Empty);
        res.Headers.Add("SID", sub.Sid);
        res.Headers.Add("TIMEOUT", sub.TimeoutHeader);
        if (string.IsNullOrEmpty(sid)) {
          events.SendInitialEvent(sub, StateFor(service));
        }
        return res;
      }

      if (request.Method == "UNSUBSCRIBE") {
        events.Unsubscribe(Header(request, "sid"));
        return new StringResponse(HttpCode.Ok, string.Empty);
      }

      throw new HttpStatusException(HttpCode.NotFound);
    }

    [SuppressMessage("ReSharper", "PossibleNullReferenceException")]
    private string GenerateDescriptor(IPAddress source)
    {
      var doc = new XmlDocument();
      doc.LoadXml(Resources.description);
      Guid guid;
      guidsForAddresses.TryGetValue(source, out guid);
      doc.SelectSingleNode("//*[local-name() = 'UDN']").InnerText =
        $"uuid:{guid}";
      doc.SelectSingleNode("//*[local-name() = 'modelNumber']").InnerText =
        Assembly.GetExecutingAssembly().GetName().Version.ToString();
      // Verbatim: this is the name the client shows in its device list, so it
      // gets no branding suffix.
      doc.SelectSingleNode("//*[local-name() = 'friendlyName']").InnerText =
        FriendlyName;

      doc.SelectSingleNode(
        "//*[text() = 'urn:schemas-upnp-org:service:ContentDirectory:1']/../*[local-name() = 'SCPDURL']").InnerText =
        $"{Prefix}contentDirectory.xml";
      doc.SelectSingleNode(
        "//*[text() = 'urn:schemas-upnp-org:service:ContentDirectory:1']/../*[local-name() = 'controlURL']").InnerText =
        $"{Prefix}control";
      // First eventSubURL in the document belongs to ContentDirectory.
      doc.SelectSingleNode("//*[local-name() = 'eventSubURL']").InnerText =
        $"{Prefix}events/{CONTENT_DIRECTORY}";

      doc.SelectSingleNode(
        "//*[text() = 'urn:schemas-upnp-org:service:ConnectionManager:1']/../*[local-name() = 'SCPDURL']").InnerText =
        $"{Prefix}connectionManager.xml";
      doc.SelectSingleNode(
        "//*[text() = 'urn:schemas-upnp-org:service:ConnectionManager:1']/../*[local-name() = 'controlURL']").InnerText
        =
        $"{Prefix}control";
      doc.SelectSingleNode(
        "//*[text() = 'urn:schemas-upnp-org:service:ConnectionManager:1']/../*[local-name() = 'eventSubURL']").InnerText
        =
        $"{Prefix}events/{CONNECTION_MANAGER}";

      doc.SelectSingleNode(
        "//*[text() = 'urn:schemas-upnp-org:service:X_MS_MediaReceiverRegistrar:1']/../*[local-name() = 'SCPDURL']")
        .InnerText =
        $"{Prefix}MSMediaReceiverRegistrar.xml";
      doc.SelectSingleNode(
        "//*[text() = 'urn:schemas-upnp-org:service:X_MS_MediaReceiverRegistrar:1']/../*[local-name() = 'controlURL']")
        .InnerText =
        $"{Prefix}control";
      doc.SelectSingleNode(
        "//*[text() = 'urn:schemas-upnp-org:service:X_MS_MediaReceiverRegistrar:1']/../*[local-name() = 'eventSubURL']")
        .InnerText =
        $"{Prefix}events/{MEDIA_RECEIVER_REGISTRAR}";

      return doc.OuterXml;
    }

    public void AddDeviceGuid(Guid guid, IPAddress address)
    {
      guidsForAddresses.Add(address, guid);
    }
  }
}

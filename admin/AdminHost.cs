using System;
using System.Text.Json;
using log4net;
using NMaier.SimpleDlna.Admin.Api;
using NMaier.SimpleDlna.Admin.Http;

namespace NMaier.SimpleDlna.Admin
{
  /// <summary>
  ///   The admin interface: loopback listener, REST API and embedded web UI.
  /// </summary>
  /// <remarks>
  ///   Constructed by both front ends, which is what keeps the console and the
  ///   tray host behaving identically.
  /// </remarks>
  public sealed class AdminHost : IDisposable
  {
    public const int DEFAULT_PORT = 19199;

    private static readonly ILog log = LogManager.GetLogger(typeof (AdminHost));

    private readonly ApiHandler api;

    private readonly AdminContext context;

    private readonly EventHub events = new EventHub();

    private readonly AdminServer server;

    private readonly WebAssets web = new WebAssets();

    private bool disposed;

    public AdminHost(AdminContext context, int port = DEFAULT_PORT)
    {
      this.context = context ??
                     throw new ArgumentNullException(nameof(context));
      context.Events = events;
      api = new ApiHandler(context);

      server = new AdminServer(port, Handle);
      context.AdminPort = server.Port;

      if (context.Manager != null) {
        context.Manager.StateChanged += OnServerStateChanged;
        context.Manager.ListChanged += OnServerListChanged;
      }
      if (context.Http != null) {
        context.Http.Playback.Changed += OnPlaybackChanged;
      }
      if (!web.HasUi) {
        log.Warn(
          "No web UI is embedded in this build; only the API is available");
      }
    }

    public int Port => server.Port;

    public string Url => server.Url;

    public bool HasUi => web.HasUi;

    public void Dispose()
    {
      if (disposed) {
        return;
      }
      disposed = true;
      if (context.Manager != null) {
        context.Manager.StateChanged -= OnServerStateChanged;
        context.Manager.ListChanged -= OnServerListChanged;
      }
      if (context.Http != null) {
        try {
          context.Http.Playback.Changed -= OnPlaybackChanged;
        }
        catch (Exception) {
          // ignored
        }
      }
      server.Dispose();
      events.Dispose();
    }

    private AdminResponse Handle(AdminRequest request)
    {
      if (request.Path.Equals(ApiHandler.PREFIX,
            StringComparison.OrdinalIgnoreCase) ||
          request.Path.StartsWith(ApiHandler.PREFIX + "/",
            StringComparison.OrdinalIgnoreCase)) {
        return api.Handle(request);
      }
      if (request.Path.StartsWith("/api/",
        StringComparison.OrdinalIgnoreCase)) {
        return ApiHandler.Error(404, "not_found",
          "Unknown API version. This server speaks /api/v1.");
      }
      return web.Handle(request);
    }

    private void OnServerStateChanged(object sender,
      ServerStateChangedEventArgs e)
    {
      Publish("servers", new
      {
        id = e.Server.Id.ToString(),
        state = e.Server.State.ToString().ToLowerInvariant()
      });
    }

    private void OnServerListChanged(object sender, EventArgs e)
    {
      Publish("servers", new {changed = "list"});
    }

    private void OnPlaybackChanged(object sender, EventArgs e)
    {
      var monitor = context.Http?.Playback;
      Publish("playback", new {playing = monitor != null && monitor.IsPlaying});
    }

    private void Publish(string name, object payload)
    {
      try {
        events.Publish(
          name, JsonSerializer.Serialize(payload, ApiHandler.JsonOptions));
      }
      catch (Exception ex) {
        log.Debug("Failed to publish an event", ex);
      }
    }
  }
}

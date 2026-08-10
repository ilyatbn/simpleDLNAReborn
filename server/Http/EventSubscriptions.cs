using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using NMaier.SimpleDlna.Utilities;
// This namespace has its own HttpClient and HttpMethod, so the BCL ones are
// reached through an alias rather than a plain using.
using Net = System.Net.Http;
using Timer = System.Timers.Timer;

namespace NMaier.SimpleDlna.Server
{
  /// <summary>
  ///   UPnP GENA eventing: keeps track of who has SUBSCRIBEd to a service and
  ///   pushes NOTIFY requests at them when state changes.
  /// </summary>
  /// <remarks>
  ///   Without this a control point never learns that the library changed. The
  ///   server used to answer SUBSCRIBE with a made-up SID and then stay silent
  ///   forever, so clients such as an LG TV sat waiting for an event that could
  ///   not arrive and only picked up changes when the user forced a re-browse.
  /// </remarks>
  internal sealed class EventSubscriptions : Logging, IDisposable
  {
    private const int DEFAULT_TIMEOUT = 1800;

    private const int MIN_TIMEOUT = 60;

    private const int MAX_TIMEOUT = 7200;

    private static readonly Net::HttpClient notifier = CreateNotifier();

    private readonly ConcurrentDictionary<string, Subscription> subscriptions =
      new ConcurrentDictionary<string, Subscription>(StringComparer.Ordinal);

    private readonly Timer reaper = new Timer(
      TimeSpan.FromSeconds(30).TotalMilliseconds);

    public EventSubscriptions()
    {
      reaper.Elapsed += Reap;
      reaper.Enabled = true;
    }

    public void Dispose()
    {
      reaper.Elapsed -= Reap;
      reaper.Dispose();
      subscriptions.Clear();
    }

    private static Net::HttpClient CreateNotifier()
    {
      // A control point that has gone away must not hold up a rescan, hence the
      // short timeout. Redirects are meaningless for event callbacks.
      var handler = new Net::HttpClientHandler {AllowAutoRedirect = false};
      return new Net::HttpClient(handler)
      {
        Timeout = TimeSpan.FromSeconds(10)
      };
    }

    /// <summary>
    ///   Handles a fresh SUBSCRIBE. Returns null when the request is not a valid
    ///   subscription (no usable CALLBACK).
    /// </summary>
    public Subscription Subscribe(string service, string callback,
      string timeout)
    {
      var callbacks = ParseCallbacks(callback);
      if (callbacks.Length == 0) {
        return null;
      }
      var sub = new Subscription
      {
        Sid = $"uuid:{Guid.NewGuid()}",
        Service = service,
        Callbacks = callbacks,
        Timeout = ParseTimeout(timeout)
      };
      sub.Renew();
      subscriptions[sub.Sid] = sub;
      InfoFormat(
        "New {0} event subscription {1} for {2}",
        service, sub.Sid, callbacks[0]);
      return sub;
    }

    /// <summary>Handles a SUBSCRIBE that carries an existing SID.</summary>
    public Subscription Renew(string sid, string timeout)
    {
      Subscription sub;
      if (string.IsNullOrEmpty(sid) ||
          !subscriptions.TryGetValue(sid, out sub)) {
        return null;
      }
      sub.Timeout = ParseTimeout(timeout);
      sub.Renew();
      DebugFormat("Renewed event subscription {0}", sid);
      return sub;
    }

    public bool Unsubscribe(string sid)
    {
      Subscription ignored;
      if (string.IsNullOrEmpty(sid) ||
          !subscriptions.TryRemove(sid, out ignored)) {
        return false;
      }
      InfoFormat("Removed event subscription {0}", sid);
      return true;
    }

    /// <summary>
    ///   Sends the initial event, which the spec requires immediately after a
    ///   successful SUBSCRIBE and which must carry every evented variable.
    /// </summary>
    public void SendInitialEvent(Subscription sub,
      IEnumerable<KeyValuePair<string, string>> properties)
    {
      Send(sub, properties.ToList());
    }

    /// <summary>Pushes a property change to every subscriber of a service.</summary>
    public void Notify(string service,
      IEnumerable<KeyValuePair<string, string>> properties)
    {
      var body = properties.ToList();
      var targets = subscriptions.Values
        .Where(s => string.Equals(s.Service, service, StringComparison.Ordinal))
        .ToList();
      if (targets.Count == 0) {
        return;
      }
      DebugFormat(
        "Notifying {0} {1} subscriber(s)", targets.Count, service);
      foreach (var sub in targets) {
        Send(sub, body);
      }
    }

    private void Send(Subscription sub,
      IList<KeyValuePair<string, string>> properties)
    {
      var seq = sub.NextSeq();
      var xml = BuildPropertySet(properties);
      // Fire and forget: a wedged or vanished control point must never stall
      // the filesystem watcher that triggered this.
      Task.Run(async () =>
      {
        foreach (var callback in sub.Callbacks) {
          try {
            using (var request = new Net::HttpRequestMessage(
              new Net::HttpMethod("NOTIFY"), callback)) {
              request.Content = new Net::StringContent(
                xml, Encoding.UTF8, "text/xml");
              request.Headers.TryAddWithoutValidation("NT", "upnp:event");
              request.Headers.TryAddWithoutValidation("NTS", "upnp:propchange");
              request.Headers.TryAddWithoutValidation("SID", sub.Sid);
              request.Headers.TryAddWithoutValidation(
                "SEQ", seq.ToString());
              using (var response = await notifier.SendAsync(request)
                .ConfigureAwait(false)) {
                if (!response.IsSuccessStatusCode) {
                  DebugFormat(
                    "Event callback {0} answered {1}",
                    callback, (int)response.StatusCode);
                  continue;
                }
              }
            }
            // One reachable callback is enough; the rest are alternates.
            return;
          }
          catch (Exception ex) {
            DebugFormat(
              "Failed to notify {0} ({1})", callback, ex.Message);
          }
        }
        // Every callback failed. Drop the subscription so a stale client does
        // not get retried on every single change for the next half hour.
        if (subscriptions.TryRemove(sub.Sid, out sub)) {
          InfoFormat("Dropped unreachable event subscription {0}", sub.Sid);
        }
      });
    }

    private static string BuildPropertySet(
      IEnumerable<KeyValuePair<string, string>> properties)
    {
      var sb = new StringBuilder();
      sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
      sb.Append(
        "<e:propertyset xmlns:e=\"urn:schemas-upnp-org:event-1-0\">");
      foreach (var p in properties) {
        sb.Append("<e:property><");
        sb.Append(p.Key);
        sb.Append('>');
        sb.Append(System.Security.SecurityElement.Escape(p.Value) ?? string.Empty);
        sb.Append("</");
        sb.Append(p.Key);
        sb.Append("></e:property>");
      }
      sb.Append("</e:propertyset>");
      return sb.ToString();
    }

    /// <summary>
    ///   CALLBACK is one or more angle-bracketed absolute URLs, e.g.
    ///   <c>&lt;http://192.168.0.5:1234/evt&gt;&lt;http://...&gt;</c>.
    /// </summary>
    private static Uri[] ParseCallbacks(string callback)
    {
      if (string.IsNullOrWhiteSpace(callback)) {
        return new Uri[0];
      }
      var rv = new List<Uri>();
      var start = callback.IndexOf('<');
      while (start >= 0) {
        var end = callback.IndexOf('>', start + 1);
        if (end < 0) {
          break;
        }
        Uri uri;
        var candidate = callback.Substring(start + 1, end - start - 1).Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) {
          rv.Add(uri);
        }
        start = callback.IndexOf('<', end + 1);
      }
      return rv.ToArray();
    }

    /// <summary>Parses a <c>Second-1800</c> / <c>Second-infinite</c> header.</summary>
    private static int ParseTimeout(string timeout)
    {
      if (string.IsNullOrWhiteSpace(timeout)) {
        return DEFAULT_TIMEOUT;
      }
      var idx = timeout.LastIndexOf('-');
      if (idx < 0 || idx + 1 >= timeout.Length) {
        return DEFAULT_TIMEOUT;
      }
      int seconds;
      if (!int.TryParse(timeout.Substring(idx + 1), out seconds)) {
        // "Second-infinite" - the spec allows it, but an infinite subscription
        // from a TV that gets unplugged never goes away, so cap it.
        return MAX_TIMEOUT;
      }
      return Math.Min(Math.Max(seconds, MIN_TIMEOUT), MAX_TIMEOUT);
    }

    private void Reap(object sender, ElapsedEventArgs e)
    {
      foreach (var sub in subscriptions.Values.Where(s => s.Expired).ToList()) {
        Subscription ignored;
        if (subscriptions.TryRemove(sub.Sid, out ignored)) {
          DebugFormat("Event subscription {0} timed out", sub.Sid);
        }
      }
    }

    internal sealed class Subscription
    {
      private long seq;

      public string Sid { get; set; }

      public string Service { get; set; }

      public Uri[] Callbacks { get; set; }

      public int Timeout { get; set; }

      public DateTime Expires { get; private set; }

      public bool Expired => DateTime.UtcNow > Expires;

      public string TimeoutHeader => $"Second-{Timeout}";

      public void Renew()
      {
        Expires = DateTime.UtcNow.AddSeconds(Timeout);
      }

      /// <summary>
      ///   SEQ 0 is reserved for the initial event; subsequent ones count up.
      /// </summary>
      public long NextSeq()
      {
        return System.Threading.Interlocked.Increment(ref seq) - 1;
      }
    }
  }
}

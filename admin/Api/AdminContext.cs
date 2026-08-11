using System;
using NMaier.SimpleDlna.Server;

namespace NMaier.SimpleDlna.Admin.Api
{
  /// <summary>
  ///   Everything the API needs from its host.
  /// </summary>
  /// <remarks>
  ///   Autostart is passed as delegates rather than referenced directly: it is
  ///   a registry Run key, which only the tray host has any business writing.
  /// </remarks>
  public sealed class AdminContext
  {
    public HttpServer Http { get; set; }

    public ServerManager Manager { get; set; }

    public SettingsStore Settings { get; set; }

    public EventHub Events { get; set; }

    /// <summary>
    ///   False when servers come from the command line. Mutating endpoints then
    ///   answer 409 rather than fighting whoever wrote the command line.
    /// </summary>
    public bool Managed { get; set; } = true;

    /// <summary>"tray" or "console".</summary>
    public string HostKind { get; set; } = "console";

    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    public int AdminPort { get; set; }

    public Func<bool> GetAutostart { get; set; }

    public Action<bool> SetAutostart { get; set; }
  }
}

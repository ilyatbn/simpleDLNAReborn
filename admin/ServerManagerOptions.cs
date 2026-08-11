using System;
using System.IO;
using NMaier.SimpleDlna.FileMediaServer;

namespace NMaier.SimpleDlna.Admin
{
  /// <summary>
  ///   Host-supplied knobs read whenever a server is started.
  /// </summary>
  /// <remarks>
  ///   Mutable and held by reference, which is what makes the settings dialog's
  ///   "(Applies when a server is restarted)" wording true: changing a value
  ///   here affects the next start, not the running server.
  /// </remarks>
  public sealed class ServerManagerOptions
  {
    /// <summary>
    ///   SQLite metadata cache, or null to run without one.
    /// </summary>
    public FileInfo CacheFile { get; set; }

    public TimeSpan ChangeDelay { get; set; } = FileServer.DefaultChangeDelay;

    public TimeSpan RescanInterval { get; set; } =
      FileServer.DefaultRescanInterval;
  }
}

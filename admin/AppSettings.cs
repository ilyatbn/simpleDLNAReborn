using System;

namespace NMaier.SimpleDlna.Admin
{
  /// <summary>
  ///   Global settings, persisted as settings.json.
  /// </summary>
  /// <remarks>
  ///   Replaces the user-scoped Properties.Settings the WinForms GUI used:
  ///   user.config is only reachable from a desktop host, and the console
  ///   server has no equivalent.
  /// </remarks>
  public sealed class AppSettings
  {
    public const string DefaultLogLevel = "Error";

    public static readonly string[] LogLevels =
    {
      "None", "Fatal", "Error", "Warn", "Info", "Debug"
    };

    /// <summary>DLNA server port. 0 picks a free one at startup.</summary>
    public int Port { get; set; }

    /// <summary>Cache directory, or empty for the default.</summary>
    public string CacheDir { get; set; } = string.Empty;

    public int RescanDelaySeconds { get; set; } = 5;

    public int RescanIntervalMinutes { get; set; } = 30;

    public string LogLevel { get; set; } = DefaultLogLevel;

    public bool StartMinimized { get; set; }

    public bool PreventSleep { get; set; }

    public AppSettings Clone()
    {
      return new AppSettings
      {
        Port = Port,
        CacheDir = CacheDir,
        RescanDelaySeconds = RescanDelaySeconds,
        RescanIntervalMinutes = RescanIntervalMinutes,
        LogLevel = LogLevel,
        StartMinimized = StartMinimized,
        PreventSleep = PreventSleep
      };
    }

    /// <summary>
    ///   Clamps every value to the range the old settings dialog enforced with
    ///   its NumericUpDown limits.
    /// </summary>
    public void Clamp()
    {
      if (Port < 0 || Port > 65535) {
        Port = 0;
      }
      if (RescanDelaySeconds < 1) {
        RescanDelaySeconds = 1;
      }
      if (RescanDelaySeconds > 3600) {
        RescanDelaySeconds = 3600;
      }
      if (RescanIntervalMinutes < 0) {
        RescanIntervalMinutes = 0;
      }
      if (RescanIntervalMinutes > 1440) {
        RescanIntervalMinutes = 1440;
      }
      CacheDir = CacheDir ?? string.Empty;
      if (Array.IndexOf(LogLevels, LogLevel) < 0) {
        LogLevel = DefaultLogLevel;
      }
    }
  }
}

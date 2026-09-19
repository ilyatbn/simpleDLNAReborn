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

    public const string DefaultNetworkChangeAction = "readvertise";

    /// <summary>
    ///   <c>none</c> leaves the old behaviour: the mounts keep advertising the
    ///   address they were started on. <c>readvertise</c> re-announces every
    ///   mount on the new addresses without touching the media servers, so no
    ///   library is rescanned. <c>restart</c> stops and starts every running
    ///   server, which also re-announces but pays for a full reload.
    /// </summary>
    public static readonly string[] NetworkChangeActions =
    {
      "none", "readvertise", "restart"
    };

    public const string DefaultNetworkChangeSignal = "both";

    /// <summary>
    ///   <c>ip</c> watches the usable IPv4 addresses and their gateways, which
    ///   covers Wi-Fi, Ethernet, docks and VPNs. <c>ssid</c> watches the
    ///   wireless network name, which catches a hop between two networks that
    ///   lease the same address. <c>both</c> is the default because neither
    ///   subsumes the other.
    /// </summary>
    public static readonly string[] NetworkChangeSignals =
    {
      "none", "ip", "ssid", "both"
    };

    /// <summary>DLNA server port. 0 picks a free one at startup.</summary>
    public int Port { get; set; }

    /// <summary>Cache directory, or empty for the default.</summary>
    public string CacheDir { get; set; } = string.Empty;

    public int RescanDelaySeconds { get; set; } = 5;

    public int RescanIntervalMinutes { get; set; } = 30;

    public string LogLevel { get; set; } = DefaultLogLevel;

    /// <summary>
    ///   What to do when the machine changes network. See
    ///   <see cref="NetworkChangeActions" />.
    /// </summary>
    public string NetworkChangeAction { get; set; } = DefaultNetworkChangeAction;

    /// <summary>
    ///   Which signals count as a network change. See
    ///   <see cref="NetworkChangeSignals" />.
    /// </summary>
    public string NetworkChangeSignal { get; set; } = DefaultNetworkChangeSignal;

    /// <summary>
    ///   How long the network has to hold still before acting. A Wi-Fi switch
    ///   goes through several intermediate states, so acting on the first one
    ///   would just mean acting again a second later.
    /// </summary>
    public int NetworkSettleSeconds { get; set; } = 6;

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
        NetworkChangeAction = NetworkChangeAction,
        NetworkChangeSignal = NetworkChangeSignal,
        NetworkSettleSeconds = NetworkSettleSeconds,
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
      if (NetworkSettleSeconds < 1) {
        NetworkSettleSeconds = 1;
      }
      if (NetworkSettleSeconds > 300) {
        NetworkSettleSeconds = 300;
      }
      CacheDir = CacheDir ?? string.Empty;
      if (Array.IndexOf(LogLevels, LogLevel) < 0) {
        LogLevel = DefaultLogLevel;
      }
      if (Array.IndexOf(NetworkChangeActions, NetworkChangeAction) < 0) {
        NetworkChangeAction = DefaultNetworkChangeAction;
      }
      if (Array.IndexOf(NetworkChangeSignals, NetworkChangeSignal) < 0) {
        NetworkChangeSignal = DefaultNetworkChangeSignal;
      }
    }
  }
}

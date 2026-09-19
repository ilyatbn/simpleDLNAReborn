using System;
using System.Threading;
using log4net;
using NMaier.SimpleDlna.Utilities;

namespace NMaier.SimpleDlna.Admin
{
  /// <summary>
  ///   Turns a network change into whatever <see cref="AppSettings" /> says
  ///   should happen to the running servers.
  /// </summary>
  /// <remarks>
  ///   <para>
  ///     Mounts advertise the address they were started on. Move the machine to
  ///     another network - a different Wi-Fi SSID, a dock, a VPN, a DHCP lease
  ///     that lands in another subnet - and the SSDP alive notifications start
  ///     failing to bind with WSAEADDRNOTAVAIL, the LOCATION URLs point at an
  ///     address nobody can reach, and the server quietly stops appearing on
  ///     any TV until it is restarted by hand.
  ///   </para>
  ///   <para>
  ///     Two remedies, because they cost very different amounts:
  ///     <c>readvertise</c> rebuilds only the announcement, which is all that
  ///     actually went stale and takes milliseconds; <c>restart</c> stops and
  ///     starts every running server, reloading each library, which is the
  ///     bigger hammer for a server that is wedged for some other reason too.
  ///   </para>
  ///   <para>
  ///     UI-free, like <see cref="ServerManager" />, so the console and the tray
  ///     app get identical behaviour.
  ///   </para>
  /// </remarks>
  public sealed class NetworkWatcher : IDisposable
  {
    private static readonly ILog log =
      LogManager.GetLogger(typeof (NetworkWatcher));

    private readonly ServerManager manager;

    private readonly NetworkMonitor monitor;

    private readonly SettingsStore settings;

    private int busy;

    private bool disposed;

    public NetworkWatcher(ServerManager manager, SettingsStore settings)
    {
      this.manager = manager ??
                     throw new ArgumentNullException(nameof(manager));
      this.settings = settings ??
                      throw new ArgumentNullException(nameof(settings));
      monitor = new NetworkMonitor();
      monitor.Changed += OnNetworkChanged;
    }

    /// <summary>
    ///   Begins watching, unless the settings switch it off. Applying the
    ///   settings is separate from construction so the caller can start the
    ///   watcher after the servers have loaded.
    /// </summary>
    public void Start()
    {
      var s = settings.Current;
      if (Action(s) == NetworkChangeAction.None) {
        log.Debug("Not watching the network: no action is configured");
        return;
      }
      var signals = Signals(s);
      if (signals == NetworkSignals.None) {
        log.Debug("Not watching the network: no signal is configured");
        return;
      }
      monitor.Signals = signals;
      monitor.SettleDelay = TimeSpan.FromSeconds(s.NetworkSettleSeconds);
      monitor.Start();
    }

    /// <summary>
    ///   Picks up changed settings. Safe to call at any time.
    /// </summary>
    /// <remarks>
    ///   Switching the action off takes effect immediately because the action
    ///   is read when a change arrives, not when the watcher starts. Switching
    ///   every signal off leaves the monitor running against an empty
    ///   fingerprint, which never changes, so it reports nothing.
    /// </remarks>
    public void Reconfigure()
    {
      if (disposed) {
        return;
      }
      var s = settings.Current;
      monitor.Signals = Signals(s);
      monitor.SettleDelay = TimeSpan.FromSeconds(s.NetworkSettleSeconds);
      // Idempotent; only does anything if the watcher was off until now.
      Start();
    }

    public void Dispose()
    {
      if (disposed) {
        return;
      }
      disposed = true;
      monitor.Changed -= OnNetworkChanged;
      monitor.Dispose();
    }

    private void OnNetworkChanged(object sender, NetworkChangedEventArgs e)
    {
      var action = Action(settings.Current);
      if (action == NetworkChangeAction.None) {
        // Turned off after the monitor started. Nothing to undo.
        return;
      }
      // A restart can outlast the settle delay on a large library, and the
      // monitor raises from a timer thread, so overlapping runs are possible.
      if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) {
        log.Info("A network change is still being handled; skipping this one");
        return;
      }
      try {
        Apply(action);
      }
      catch (Exception ex) {
        log.Error("Failed to handle the network change", ex);
      }
      finally {
        Interlocked.Exchange(ref busy, 0);
      }
    }

    private void Apply(NetworkChangeAction action)
    {
      switch (action) {
      case NetworkChangeAction.Restart:
        log.Info("Network changed; restarting the running servers");
        var result = manager.RestartAll();
        log.InfoFormat("Restarted {0} server(s), {1} failed",
          result.Restarted, result.Failed);
        break;
      case NetworkChangeAction.Readvertise:
        log.Info("Network changed; re-announcing the mounts");
        manager.Readvertise();
        break;
      }
    }

    private static NetworkChangeAction Action(AppSettings s)
    {
      switch (s.NetworkChangeAction) {
      case "restart":
        return NetworkChangeAction.Restart;
      case "none":
        return NetworkChangeAction.None;
      default:
        return NetworkChangeAction.Readvertise;
      }
    }

    private static NetworkSignals Signals(AppSettings s)
    {
      switch (s.NetworkChangeSignal) {
      case "none":
        return NetworkSignals.None;
      case "ip":
        return NetworkSignals.Addresses;
      case "ssid":
        return NetworkSignals.Ssid;
      default:
        return NetworkSignals.Both;
      }
    }

    private enum NetworkChangeAction
    {
      None,
      Readvertise,
      Restart
    }
  }
}

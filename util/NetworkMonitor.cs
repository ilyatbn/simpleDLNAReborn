using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Timers;
using Timer = System.Timers.Timer;

namespace NMaier.SimpleDlna.Utilities
{
  /// <summary>
  ///   Which signals <see cref="NetworkMonitor" /> treats as a network change.
  /// </summary>
  [Flags]
  public enum NetworkSignals
  {
    None = 0,

    /// <summary>
    ///   The set of usable IPv4 addresses and their gateways. Covers Wi-Fi,
    ///   Ethernet, docking stations, VPNs and DHCP leases that move.
    /// </summary>
    Addresses = 1,

    /// <summary>
    ///   The SSIDs of the connected wireless interfaces. Catches the case
    ///   <see cref="Addresses" /> cannot see: two networks that hand out the
    ///   same address, which is not rare given how many routers default to
    ///   192.168.1.0/24.
    /// </summary>
    Ssid = 2,

    Both = Addresses | Ssid
  }

  public sealed class NetworkChangedEventArgs : EventArgs
  {
    internal NetworkChangedEventArgs(string previous, string current)
    {
      Previous = previous;
      Current = current;
    }

    /// <summary>The fingerprint that was in force until now.</summary>
    public string Previous { get; }

    /// <summary>The fingerprint that replaced it.</summary>
    public string Current { get; }
  }

  /// <summary>
  ///   Watches the network identity of this machine and raises
  ///   <see cref="Changed" /> once it has settled on something new.
  /// </summary>
  /// <remarks>
  ///   <para>
  ///     Three things make this more than a <see cref="NetworkChange" />
  ///     subscription:
  ///   </para>
  ///   <list type="number">
  ///     <item>
  ///       Windows fires <c>NetworkAddressChanged</c> many times through a
  ///       single Wi-Fi transition - link down, link up, APIPA, DHCP offer,
  ///       DHCP ack - so the events are coalesced behind a settle timer and
  ///       only a genuinely different fingerprint is reported.
  ///     </item>
  ///     <item>
  ///       Not every relevant change raises an event at all. An SSID change
  ///       that keeps the same address raises nothing, so there is a poll as
  ///       well; it costs one enumeration of the adapters.
  ///     </item>
  ///     <item>
  ///       Mid-transition the machine has no usable address. Reporting then
  ///       would hand the consumer an empty address list, so the settle timer
  ///       is restarted until something usable appears.
  ///     </item>
  ///   </list>
  /// </remarks>
  public sealed class NetworkMonitor : Logging, IDisposable
  {
    private readonly Timer poll;

    private readonly Timer settle;

    private readonly object sync = new object();

    private bool disposed;

    private string fingerprint;

    private bool started;

    public NetworkMonitor()
    {
      settle = new Timer {AutoReset = false};
      settle.Elapsed += OnSettled;
      poll = new Timer {AutoReset = true};
      poll.Elapsed += OnPoll;
      SettleDelay = TimeSpan.FromSeconds(6);
      PollInterval = TimeSpan.FromSeconds(20);
    }

    /// <summary>
    ///   What counts as a change. <see cref="NetworkSignals.None" /> disables
    ///   the monitor; it must be set before <see cref="Start" />.
    /// </summary>
    public NetworkSignals Signals { get; set; } = NetworkSignals.Both;

    /// <summary>
    ///   How long the network has to hold still before a change is reported.
    ///   Restarted by every further event, so a noisy transition reports once.
    /// </summary>
    public TimeSpan SettleDelay
    {
      get { return TimeSpan.FromMilliseconds(settle.Interval); }
      set { settle.Interval = Math.Max(250, value.TotalMilliseconds); }
    }

    /// <summary>Backstop for changes that raise no event of their own.</summary>
    public TimeSpan PollInterval
    {
      get { return TimeSpan.FromMilliseconds(poll.Interval); }
      set { poll.Interval = Math.Max(1000, value.TotalMilliseconds); }
    }

    /// <summary>The fingerprint the last reported change settled on.</summary>
    public string Current
    {
      get {
        lock (sync) {
          return fingerprint;
        }
      }
    }

    /// <summary>
    ///   Raised on a timer thread once the network has settled on a fingerprint
    ///   different from the previous one. Never raised for the initial state.
    /// </summary>
    public event EventHandler<NetworkChangedEventArgs> Changed;

    public void Start()
    {
      lock (sync) {
        if (started || disposed || Signals == NetworkSignals.None) {
          return;
        }
        started = true;
        fingerprint = Fingerprint();
      }
      NetworkChange.NetworkAddressChanged += OnAddressChanged;
      NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
      poll.Enabled = true;
      NoticeFormat("Watching the network for changes ({0}); currently {1}",
        Signals, Current);
    }

    public void Dispose()
    {
      bool wasStarted;
      lock (sync) {
        if (disposed) {
          return;
        }
        disposed = true;
        wasStarted = started;
      }
      if (wasStarted) {
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
      }
      poll.Enabled = false;
      settle.Enabled = false;
      poll.Dispose();
      settle.Dispose();
    }

    private void OnAddressChanged(object sender, EventArgs e)
    {
      Debug("Network address change signalled");
      Schedule();
    }

    private void OnAvailabilityChanged(object sender,
      NetworkAvailabilityEventArgs e)
    {
      DebugFormat("Network availability signalled: {0}", e.IsAvailable);
      Schedule();
    }

    private void OnPoll(object sender, ElapsedEventArgs e)
    {
      // Nothing to do while a transition is already being waited out.
      if (settle.Enabled) {
        return;
      }
      try {
        if (Fingerprint() != Current) {
          Debug("Network change noticed by polling");
          Schedule();
        }
      }
      catch (Exception ex) {
        Debug("Failed to poll the network state", ex);
      }
    }

    /// <summary>Restarts the settle timer, so a burst of events reports once.</summary>
    private void Schedule()
    {
      lock (sync) {
        if (disposed) {
          return;
        }
      }
      try {
        settle.Enabled = false;
        settle.Enabled = true;
      }
      catch (ObjectDisposedException) {
      }
    }

    private void OnSettled(object sender, ElapsedEventArgs e)
    {
      string previous;
      string current;
      try {
        current = Fingerprint();
        if (!HasUsableAddress()) {
          // Still mid-transition. Wait for the next event, or for the poll to
          // notice, rather than reporting a machine with no address.
          Debug("Network has not settled yet; no usable address");
          Schedule();
          return;
        }
        lock (sync) {
          if (disposed || current == fingerprint) {
            return;
          }
          previous = fingerprint;
          fingerprint = current;
        }
      }
      catch (Exception ex) {
        Warn("Failed to read the network state", ex);
        return;
      }

      NoticeFormat("Network changed: {0} -> {1}", previous, current);
      try {
        Changed?.Invoke(this, new NetworkChangedEventArgs(previous, current));
      }
      catch (Exception ex) {
        Error("A network change listener failed", ex);
      }
    }

    private static bool HasUsableAddress()
    {
      try {
        return IP.ExternalIPAddresses.Any();
      }
      catch (Exception) {
        return false;
      }
    }

    /// <summary>
    ///   A stable, comparable description of where this machine sits on the
    ///   network, built only from the signals that are switched on.
    /// </summary>
    private string Fingerprint()
    {
      var parts = new List<string>();
      if ((Signals & NetworkSignals.Addresses) != 0) {
        parts.Add("ip=" + string.Join(",", Addresses()));
      }
      if ((Signals & NetworkSignals.Ssid) != 0) {
        parts.Add("ssid=" + string.Join(",", Wlan.ConnectedSsids()));
      }
      return string.Join(" ", parts);
    }

    /// <summary>
    ///   Every usable IPv4 address paired with the gateway it routes through.
    ///   The gateway is what separates two different networks that happen to
    ///   lease the same address.
    /// </summary>
    private IEnumerable<string> Addresses()
    {
      var rv = new List<string>();
      NetworkInterface[] adapters;
      try {
        adapters = NetworkInterface.GetAllNetworkInterfaces();
      }
      catch (Exception ex) {
        Debug("Failed to enumerate adapters", ex);
        return rv;
      }

      foreach (var adapter in adapters) {
        if (adapter.OperationalStatus != OperationalStatus.Up) {
          continue;
        }
        IPInterfaceProperties props;
        try {
          props = adapter.GetIPProperties();
        }
        catch (Exception) {
          // An adapter can disappear between the enumeration and the read.
          continue;
        }
        var gateways = (from g in props.GatewayAddresses
                        where g.Address != null &&
                              g.Address.AddressFamily ==
                              AddressFamily.InterNetwork &&
                              !g.Address.Equals(IPAddress.Any)
                        select g.Address.ToString()).ToArray();
        if (gateways.Length == 0) {
          // Matches IP.ExternalIPAddresses: no gateway, not a network we serve.
          continue;
        }
        Array.Sort(gateways, StringComparer.Ordinal);
        foreach (var uni in props.UnicastAddresses) {
          if (uni.Address.AddressFamily != AddressFamily.InterNetwork ||
              IPAddress.IsLoopback(uni.Address)) {
            continue;
          }
          rv.Add($"{uni.Address}>{string.Join("+", gateways)}");
        }
      }
      rv.Sort(StringComparer.Ordinal);
      return rv;
    }
  }
}

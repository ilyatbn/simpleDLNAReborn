using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using log4net;

namespace NMaier.SimpleDlna.Utilities
{
  /// <summary>
  ///   The SSIDs this machine is currently associated with, if any.
  /// </summary>
  /// <remarks>
  ///   Native wlanapi rather than parsing <c>netsh wlan show interfaces</c>:
  ///   netsh output is localised, so the parse breaks on a non-English Windows,
  ///   and polling it spawns a process every time. Every failure mode here -
  ///   no wireless hardware, the WLAN AutoConfig service stopped, a Server SKU
  ///   without the feature installed - is answered with an empty list, because
  ///   the only caller uses this as one signal among several.
  /// </remarks>
  public static class Wlan
  {
    private static readonly ILog logger = LogManager.GetLogger(typeof (Wlan));

    private const uint ERROR_SUCCESS = 0;

    private const uint CLIENT_VERSION = 2;

    /// <summary>wlan_intf_opcode_current_connection</summary>
    private const uint OPCODE_CURRENT_CONNECTION = 7;

    /// <summary>wlan_interface_state_connected</summary>
    private const uint STATE_CONNECTED = 1;

    /// <summary>
    ///   Offset of WLAN_CONNECTION_ATTRIBUTES.wlanAssociationAttributes:
    ///   isState (4) + wlanConnectionMode (4) + strProfileName (256 WCHARs).
    /// </summary>
    private const int ASSOCIATION_OFFSET = 4 + 4 + 256 * 2;

    /// <summary>Longest SSID DOT11_SSID can carry.</summary>
    private const int MAX_SSID = 32;

    /// <summary>
    ///   True once a call failed, so a machine without Wi-Fi does not log the
    ///   same failure on every poll.
    /// </summary>
    private static bool unavailable;

    /// <summary>
    ///   One entry per connected wireless interface, sorted, never null.
    /// </summary>
    public static IEnumerable<string> ConnectedSsids()
    {
      if (unavailable || !OperatingSystem.IsWindows()) {
        return new string[0];
      }
      try {
        return Query();
      }
      catch (Exception ex) {
        unavailable = true;
        logger.Debug("Wi-Fi state is unavailable; SSID changes will be ignored",
          ex);
        return new string[0];
      }
    }

    private static List<string> Query()
    {
      var rv = new List<string>();
      var handle = IntPtr.Zero;
      var list = IntPtr.Zero;
      try {
        uint negotiated;
        if (WlanOpenHandle(CLIENT_VERSION, IntPtr.Zero, out negotiated,
          out handle) != ERROR_SUCCESS) {
          unavailable = true;
          return rv;
        }
        if (WlanEnumInterfaces(handle, IntPtr.Zero, out list) !=
            ERROR_SUCCESS) {
          return rv;
        }

        // WLAN_INTERFACE_INFO_LIST: dwNumberOfItems, dwIndex, then the array.
        var count = Marshal.ReadInt32(list);
        for (var i = 0; i < count; ++i) {
          var info = (WlanInterfaceInfo)Marshal.PtrToStructure(
            list + 8 + i * Marshal.SizeOf(typeof (WlanInterfaceInfo)),
            typeof (WlanInterfaceInfo));
          if (info.State != STATE_CONNECTED) {
            continue;
          }
          var ssid = CurrentSsid(handle, info.Guid);
          if (!string.IsNullOrEmpty(ssid)) {
            rv.Add(ssid);
          }
        }
      }
      finally {
        if (list != IntPtr.Zero) {
          WlanFreeMemory(list);
        }
        if (handle != IntPtr.Zero) {
          WlanCloseHandle(handle, IntPtr.Zero);
        }
      }
      rv.Sort(StringComparer.Ordinal);
      return rv;
    }

    private static string CurrentSsid(IntPtr handle, Guid iface)
    {
      var data = IntPtr.Zero;
      try {
        uint size;
        uint ignored;
        if (WlanQueryInterface(handle, ref iface, OPCODE_CURRENT_CONNECTION,
          IntPtr.Zero, out size, out data, out ignored) != ERROR_SUCCESS) {
          return null;
        }
        if (size < ASSOCIATION_OFFSET + 4 + MAX_SSID) {
          return null;
        }

        // DOT11_SSID leads the association attributes: a length then a fixed
        // 32 byte buffer that is *not* null terminated.
        var length = Marshal.ReadInt32(data, ASSOCIATION_OFFSET);
        if (length <= 0 || length > MAX_SSID) {
          return null;
        }
        var bytes = new byte[length];
        Marshal.Copy(data + ASSOCIATION_OFFSET + 4, bytes, 0, length);
        // 802.11 leaves the encoding unspecified; UTF-8 is what Windows and
        // every modern AP use, and this value is only ever compared, not shown.
        return System.Text.Encoding.UTF8.GetString(bytes);
      }
      finally {
        if (data != IntPtr.Zero) {
          WlanFreeMemory(data);
        }
      }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterfaceInfo
    {
      public Guid Guid;

      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
      public string Description;

      public uint State;
    }

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern uint WlanOpenHandle(uint clientVersion,
      IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern uint WlanCloseHandle(IntPtr clientHandle,
      IntPtr reserved);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern uint WlanEnumInterfaces(IntPtr clientHandle,
      IntPtr reserved, out IntPtr interfaceList);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern uint WlanQueryInterface(IntPtr clientHandle,
      ref Guid interfaceGuid, uint opCode, IntPtr reserved, out uint dataSize,
      out IntPtr data, out uint opCodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
  }
}

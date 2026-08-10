using System;
using System.Runtime.InteropServices;

namespace NMaier.SimpleDlna.Utilities
{
  internal static class SafeNativeMethods
  {
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    internal static extern int StrCmpLogicalW(string psz1, string psz2);

    [DllImport("iphlpapi.dll")]
    public static extern uint SendARP(
      uint destIP, uint srcIP, [Out] byte[] pMacAddr,
      ref uint phyAddrLen);

    [DllImport("libc", CharSet = CharSet.Ansi)]
    public static extern int uname(IntPtr buf);

    /// <summary>
    ///   Returns the previous state, or 0 on failure. The request applies to
    ///   the calling thread only - see <see cref="SleepInhibitor" />.
    /// </summary>
    [DllImport("kernel32.dll")]
    internal static extern ExecutionState SetThreadExecutionState(
      ExecutionState esFlags);
  }

  [Flags]
  internal enum ExecutionState : uint
  {
    None = 0,
    SystemRequired = 0x00000001,
    DisplayRequired = 0x00000002,
    AwayModeRequired = 0x00000040,
    Continuous = 0x80000000
  }
}

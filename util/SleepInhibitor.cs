using System;
using System.Runtime.InteropServices;
using System.Threading;
using log4net;

namespace NMaier.SimpleDlna.Utilities
{
  /// <summary>
  ///   Keeps the machine from going to sleep while something is happening.
  /// </summary>
  /// <remarks>
  ///   Wraps SetThreadExecutionState, which is <em>thread scoped</em>: the
  ///   request lasts only as long as the thread that made it. Calling it from a
  ///   thread-pool thread would quietly stop working the moment that thread is
  ///   recycled, so this owns a dedicated thread that lives for as long as the
  ///   inhibitor does and re-asserts the request periodically.
  /// </remarks>
  public sealed class SleepInhibitor : IDisposable
  {
    private static readonly ILog logger =
      LogManager.GetLogger(typeof (SleepInhibitor));

    private static readonly TimeSpan reassert = TimeSpan.FromSeconds(30);

    private readonly AutoResetEvent wake = new AutoResetEvent(false);

    private readonly Thread thread;

    private volatile bool disposed;

    private volatile bool inhibit;

    private bool applied;

    public SleepInhibitor()
    {
      thread = new Thread(Run)
      {
        Name = "SleepInhibitor",
        IsBackground = true
      };
      thread.Start();
    }

    /// <summary>
    ///   Set to true to keep the machine awake. Safe to set from any thread and
    ///   to set repeatedly to the same value.
    /// </summary>
    public bool Inhibit
    {
      get { return inhibit; }
      set {
        if (inhibit == value) {
          return;
        }
        inhibit = value;
        wake.Set();
      }
    }

    public void Dispose()
    {
      if (disposed) {
        return;
      }
      disposed = true;
      inhibit = false;
      wake.Set();
      // Let the thread clear the request before it exits; the state would be
      // released on thread exit anyway, this just makes it deterministic.
      thread.Join(TimeSpan.FromSeconds(2));
      wake.Dispose();
    }

    private void Run()
    {
      while (!disposed) {
        Apply(inhibit);
        wake.WaitOne(reassert);
      }
      Apply(false);
    }

    private void Apply(bool wanted)
    {
      if (!wanted && !applied) {
        return;
      }
      try {
        var flags = ExecutionState.Continuous;
        if (wanted) {
          // AwayModeRequired is the media-playback variant: on machines that
          // support it the box looks asleep but keeps serving. It is ignored
          // where unsupported, and SystemRequired still covers those.
          flags |= ExecutionState.SystemRequired |
                   ExecutionState.AwayModeRequired;
        }
        var previous = SafeNativeMethods.SetThreadExecutionState(flags);
        if (previous == 0) {
          logger.Warn("Failed to change the system sleep state");
          return;
        }
        applied = wanted;
        logger.InfoFormat(
          wanted
            ? "Sleep is now inhibited"
            : "Sleep is no longer inhibited");
      }
      catch (Exception ex) {
        logger.Error("Failed to change the system sleep state", ex);
      }
    }
  }
}

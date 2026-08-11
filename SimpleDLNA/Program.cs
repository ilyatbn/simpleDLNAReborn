using System;
using System.IO.Pipes;
using System.Threading;
using System.Windows.Forms;
using NMaier.SimpleDlna.Utilities;
using SystemInformation = NMaier.SimpleDlna.Utilities.SystemInformation;

namespace NMaier.SimpleDlna.GUI
{
  internal static class Program
  {
    private const string MUTEX = @"Global\simpledlnaguilock";

    private const string PIPE = "simpledlnagui";

    [STAThread]
    private static void Main()
    {
      using (var mutex = new Mutex(false, MUTEX)) {
#if !DEBUG
        if (!mutex.WaitOne(0, false)) {
          // Already running. Tell the live instance to open the web UI - the
          // browser is the window now, so there is nothing to focus here.
          using (var pipe = new NamedPipeClientStream(
            ".", PIPE, PipeDirection.Out)) {
            try {
              pipe.Connect(10000);
              pipe.WriteByte(1);
            }
            catch (Exception) {
              // ignored
            }
            return;
          }
        }
        GC.Collect();
#endif

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using (var context = new TrayContext()) {
          StartPipeNotification(context);
          try {
            Application.Run(context);
          }
          catch (Exception ex) {
            log4net.LogManager.GetLogger(typeof (Program)).Fatal(
              "Encountered fatal unhandled exception", ex);
            MessageBox.Show(
              $"Encountered an unhandled error. Will exit now.\n\n{ex.Message}\n{ex.StackTrace}",
              "Error",
              MessageBoxButtons.OK,
              MessageBoxIcon.Error
            );
            throw;
          }
        }
      }
    }

    /// <summary>
    ///   Listens for a second launch and opens the web UI when one happens.
    /// </summary>
    private static void StartPipeNotification(TrayContext context)
    {
#if DEBUG
      log4net.LogManager.GetLogger(typeof (Program)).Info(
        "Debug mode / Skipping one-instance-only stuff");
#else
      if (SystemInformation.IsRunningOnMono()) {
        // XXX Mono sometimes stack overflows for whatever reason.
        return;
      }
      new Thread(() =>
      {
        for (;;) {
          try {
            using (var pipe = new NamedPipeServerStream(
              PIPE, PipeDirection.InOut)) {
              pipe.WaitForConnection();
              pipe.ReadByte();
              context.OnSecondInstance();
            }
          }
          catch (Exception) {
            // ignored
          }
        }
        // ReSharper disable once FunctionNeverReturns
      }) {IsBackground = true}.Start();
#endif
    }
  }
}

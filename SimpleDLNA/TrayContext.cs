using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using NMaier.SimpleDlna.Admin;
using NMaier.SimpleDlna.Admin.Api;
using NMaier.SimpleDlna.Admin.Http;
using NMaier.SimpleDlna.GUI.Properties;
using NMaier.SimpleDlna.Server;
using NMaier.SimpleDlna.Utilities;

namespace NMaier.SimpleDlna.GUI
{
  /// <summary>
  ///   The whole desktop application: a tray icon that opens the web UI.
  /// </summary>
  /// <remarks>
  ///   There is no Form anywhere. An ApplicationContext gives the message loop
  ///   a NotifyIcon needs, which removes the hidden-window, minimize-to-tray and
  ///   SetVisibleCore machinery the old GUI carried purely to stay out of sight.
  /// </remarks>
  internal sealed class TrayContext : ApplicationContext
  {
    internal const string AUTOSTART_KEY = "SimpleDLNA";

    private static readonly ILog log =
      LogManager.GetLogger(typeof (TrayContext));

    private readonly AdminHost adminHost;

    private readonly HttpServer httpServer;

    private readonly ServerManager manager;

    private readonly NotifyIcon notifyIcon;

    private readonly SettingsStore settings;

    private readonly SleepInhibitor sleepInhibitor = new SleepInhibitor();

    private bool disposed;

    public TrayContext()
    {
      settings = new SettingsStore(Paths.SettingsFile);
      settings.SeedIfMissing(LegacySettings.Read());
      var current = settings.Current;

      SetupLogging(current);

      httpServer = new HttpServer(current.Port);
      var cacheDir = Paths.ResolveCacheDir(current.CacheDir);
      manager = new ServerManager(
        httpServer,
        new DescriptorStore(Paths.DescriptorsFile),
        new ServerManagerOptions
        {
#if DEBUG
          CacheFile = null,
#else
          CacheFile = Paths.CacheFile(cacheDir),
#endif
          ChangeDelay = TimeSpan.FromSeconds(current.RescanDelaySeconds),
          RescanInterval =
            TimeSpan.FromMinutes(current.RescanIntervalMinutes)
        });

      notifyIcon = new NotifyIcon
      {
        Icon = TrayIcon(),
        Text = "SimpleDLNA",
        Visible = true,
        ContextMenuStrip = BuildMenu()
      };
      notifyIcon.DoubleClick += (s, e) => OpenUi();

      settings.Changed += SettingsChanged;
      httpServer.Playback.Changed += (s, e) => UpdateSleepInhibitor();

      adminHost = StartAdmin();
      notifyIcon.Text = Truncate($"SimpleDLNA - Port {httpServer.RealPort}");

      manager.Load();
      UpdateSleepInhibitor();
    }

    /// <summary>
    ///   The executable's own icon, so the tray matches the shortcut. Falls
    ///   back to the embedded server icon if extraction fails.
    /// </summary>
    private static Icon TrayIcon()
    {
      try {
        return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ??
               Resources.server;
      }
      catch (Exception) {
        return Resources.server;
      }
    }

    private ContextMenuStrip BuildMenu()
    {
      var menu = new ContextMenuStrip();
      var open = new ToolStripMenuItem("Open SimpleDLNA", null,
        (s, e) => OpenUi())
      {
        Font = new Font(menu.Font, FontStyle.Bold)
      };
      menu.Items.Add(open);
      menu.Items.Add(new ToolStripSeparator());
      menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => Quit()));
      return menu;
    }

    private AdminHost StartAdmin()
    {
      try {
        return new AdminHost(new AdminContext
        {
          Http = httpServer,
          Manager = manager,
          Settings = settings,
          Managed = true,
          HostKind = "tray",
          GetAutostart = GetAutostart,
          SetAutostart = SetAutostart
        });
      }
      catch (AdminServerBindException ex) {
        log.Error("Could not start the admin interface", ex);
        // A busy port must not stop the media server; say so and carry on.
        notifyIcon.ShowBalloonTip(
          10000, "SimpleDLNA", ex.Message, ToolTipIcon.Warning);
        return null;
      }
      catch (Exception ex) {
        log.Error("Could not start the admin interface", ex);
        return null;
      }
    }

    private void OpenUi()
    {
      if (adminHost == null) {
        notifyIcon.ShowBalloonTip(
          8000, "SimpleDLNA",
          "The web interface is not running. See sdlna.log for details.",
          ToolTipIcon.Warning);
        return;
      }
      Shell(adminHost.Url);
    }

    /// <summary>
    ///   Hands a URL to the shell.
    /// </summary>
    /// <remarks>
    ///   UseShellExecute must be set explicitly: it defaulted to true on .NET
    ///   Framework but is false on .NET, where passing a URL to Process.Start
    ///   throws instead of opening anything.
    /// </remarks>
    internal void Shell(string target)
    {
      try {
        using (Process.Start(new ProcessStartInfo(target)
        {
          UseShellExecute = true
        })) {
        }
      }
      catch (Exception ex) {
        log.Error($"Failed to open {target}", ex);
        MessageBox.Show(
          $"Could not open {target}\n\n{ex.Message}", "SimpleDLNA",
          MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
    }

    /// <summary>Called by the pipe listener when a second instance starts.</summary>
    internal void OnSecondInstance()
    {
      OpenUi();
    }

    private void SettingsChanged(object sender, SettingsChangedEventArgs e)
    {
      var s = e.Current;
      SetupLogging(s);
      manager.Options.ChangeDelay =
        TimeSpan.FromSeconds(s.RescanDelaySeconds);
      manager.Options.RescanInterval =
        TimeSpan.FromMinutes(s.RescanIntervalMinutes);
      UpdateSleepInhibitor();
    }

    /// <summary>
    ///   The single place playback state turns into behaviour. The console gets
    ///   this too now, which it never did while it lived in the main form.
    /// </summary>
    private void UpdateSleepInhibitor()
    {
      try {
        var playing = httpServer?.Playback?.IsPlaying ?? false;
        sleepInhibitor.Inhibit = playing && settings.Current.PreventSleep;
      }
      catch (Exception ex) {
        log.Debug("Failed to update the sleep inhibitor", ex);
      }
    }

    private static bool GetAutostart()
    {
      using (var utilities = new StartupUtilities(
        StartupUtilities.StartupUserScope.CurrentUser)) {
        return utilities.CheckIfRunAtWinBoot(AUTOSTART_KEY);
      }
    }

    private static void SetAutostart(bool enabled)
    {
      using (var utilities = new StartupUtilities(
        StartupUtilities.StartupUserScope.CurrentUser)) {
        if (enabled) {
          utilities.InstallAutoRun(AUTOSTART_KEY);
        }
        else {
          utilities.UninstallAutoRun(AUTOSTART_KEY);
        }
      }
    }

    private static string Truncate(string value)
    {
      // NotifyIcon.Text throws above 63 characters.
      return value.Length <= 63 ? value : value.Substring(0, 63);
    }

    /// <summary>
    ///   Logging always goes to sdlna.log in the cache directory. Rolling is
    ///   composite - by date so yesterday's log is separate, and by size so a
    ///   single noisy day cannot fill the disk either.
    /// </summary>
    private static void SetupLogging(AppSettings settings)
    {
      var hierarchy = (Hierarchy)LogManager.GetRepository();
      // Called again whenever settings change; without resetting, every call
      // would stack another appender on the root logger.
      hierarchy.ResetConfiguration();

      var level = ToLog4NetLevel(settings.LogLevel);
      if (level == Level.Off) {
        hierarchy.Root.Level = Level.Off;
        hierarchy.Threshold = Level.Off;
        hierarchy.Configured = true;
        return;
      }

      var layout = new PatternLayout
      {
        ConversionPattern =
          "%date %6level [%3thread] %-30.30logger{1} - %message%newline%exception"
      };
      layout.ActivateOptions();
      var cacheDir = Paths.ResolveCacheDir(settings.CacheDir);
      var fileAppender = new RollingFileAppender
      {
        File = Paths.LogFile(cacheDir).FullName,
        Layout = layout,
        AppendToFile = true,
        RollingStyle = RollingFileAppender.RollingMode.Composite,
        DatePattern = "'.'yyyy-MM-dd",
        MaxSizeRollBackups = 1,
        MaximumFileSize = "5MB",
        StaticLogFileName = true,
        PreserveLogFileNameExtension = true,
        ImmediateFlush = true,
        Threshold = level
      };
      fileAppender.ActivateOptions();

      BasicConfigurator.Configure(hierarchy, fileAppender);
      hierarchy.Root.Level = level;
      hierarchy.Threshold = level;
    }

    internal static Level ToLog4NetLevel(string name)
    {
      switch (name) {
      case "None":
        return Level.Off;
      case "Fatal":
        return Level.Fatal;
      case "Warn":
        return Level.Warn;
      case "Info":
        return Level.Info;
      case "Debug":
        return Level.Debug;
      default:
        return Level.Error;
      }
    }

    private void Quit()
    {
      Dispose(true);
      ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing && !disposed) {
        disposed = true;
        try {
          notifyIcon.Visible = false;
          adminHost?.Dispose();
          settings.Changed -= SettingsChanged;
          manager?.Dispose();
          httpServer?.Dispose();
          sleepInhibitor.Dispose();
          notifyIcon.Dispose();
        }
        catch (Exception ex) {
          log.Error("Failed to shut down cleanly", ex);
        }
      }
      base.Dispose(disposing);
    }
  }

  /// <summary>
  ///   One-time import of the settings the WinForms GUI kept in user.config.
  /// </summary>
  /// <remarks>
  ///   Only read when settings.json does not exist yet. The old store is left
  ///   untouched so an older build still starts if this one is rolled back.
  /// </remarks>
  internal static class LegacySettings
  {
    public static AppSettings Read()
    {
      try {
        var config = Settings.Default;
        return new AppSettings
        {
          Port = (int)config.port,
          CacheDir = config.cache ?? string.Empty,
          RescanDelaySeconds = (int)config.rescandelay,
          RescanIntervalMinutes = (int)config.rescaninterval,
          LogLevel = config.loglevel,
          StartMinimized = config.startminimized,
          PreventSleep = config.preventsleep
        };
      }
      catch (Exception) {
        // A missing or unreadable user.config just means a fresh install.
        return null;
      }
    }
  }
}

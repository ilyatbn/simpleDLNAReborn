using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using Form = NMaier.Windows.Forms.Form;
using SystemInformation = NMaier.SimpleDlna.Utilities.SystemInformation;

namespace NMaier.SimpleDlna.GUI
{
  public partial class FormMain : Form
  {
    private const string DESCRIPTOR_FILE = "descriptors.xml";

    internal const string AUTOSTART_KEY = "SimpleDLNA";

    private bool canClose;

    private static readonly Settings config = Settings.Default;

    private readonly FileInfo cacheFile =
      new FileInfo(Path.Combine(CacheDir, "sdlna.cache"));

    private readonly FileInfo logFile =
      new FileInfo(Path.Combine(CacheDir, "sdlna.log"));

    private static readonly ILog log = LogManager.GetLogger(typeof (FormMain));

    private bool minimized = config.startminimized;

    private HttpServer httpServer;

    private ServerManager manager;

    private SettingsStore settingsStore;

    private AdminHost adminHost;

    private readonly SleepInhibitor sleepInhibitor = new SleepInhibitor();

    public FormMain()
    {
      InitializeComponent();

      listImages.Images.Add("idle", Resources.idle);
      listImages.Images.Add("active", Resources.active);
      listImages.Images.Add("inactive", Resources.inactive);
      listImages.Images.Add("refreshing", Resources.refreshing);
      listImages.Images.Add("loading", Resources.loading);
      listImages.Images.Add("info", Resources.info);
      listImages.Images.Add("warn", Resources.warn);
      listImages.Images.Add("error", Resources.error);
      listImages.Images.Add("server", Resources.server.ToBitmap());

      SetupLogging();

      StartPipeNotification();

      notifyIcon.Icon = Icon;
      if (!string.IsNullOrWhiteSpace(config.cache)) {
        cacheFile = new FileInfo(config.cache);
      }
      preventSleepToolStripMenuItem.Checked = config.preventsleep;

      CreateHandle();
      SetupServer();

      httpServer.Playback.Changed += PlaybackChanged;
      UpdatePlaybackState();
    }

    /// <summary>
    ///   Single place where playback state is turned into behaviour, so further
    ///   consumers of <see cref="PlaybackMonitor" /> can just be added here.
    /// </summary>
    private void UpdatePlaybackState()
    {
      var monitor = httpServer?.Playback;
      var playing = monitor != null && monitor.IsPlaying;

      sleepInhibitor.Inhibit = playing && config.preventsleep;

      var session = monitor?.Current;
      if (playing && session != null) {
        statusPlayback.Image = Resources.active;
        statusPlayback.Text = $"Playing: {session.Title} — {session.Client}";
      }
      else {
        statusPlayback.Image = Resources.idle;
        statusPlayback.Text = "Nothing playing";
      }
    }

    private void PlaybackChanged(object sender, EventArgs e)
    {
      // Raised from a stream or timer thread.
      if (!IsHandleCreated || IsDisposed) {
        return;
      }
      try {
        BeginInvoke((Action)UpdatePlaybackState);
      }
      catch (ObjectDisposedException) {
        // Racing a shutdown.
      }
      catch (InvalidOperationException) {
        // Handle went away between the check and the call.
      }
    }

    private void preventSleepToolStripMenuItem_CheckedChanged(object sender,
      EventArgs e)
    {
      config.preventsleep = preventSleepToolStripMenuItem.Checked;
      config.Save();
      UpdatePlaybackState();
    }

    protected sealed override void CreateHandle()
    {
      base.CreateHandle();
    }

    private static string CacheDir
    {
      get {
        var rv = config.cache;
        if (!string.IsNullOrWhiteSpace(rv) && Directory.Exists(rv)) {
          return rv;
        }
        try {
          try {
            rv = Environment.GetFolderPath(
              Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(rv)) {
              throw new IOException("Cannot get LocalAppData");
            }
          }
          catch (Exception) {
            rv = Environment.GetFolderPath(
              Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(rv)) {
              throw new IOException("Cannot get LocalAppData");
            }
          }
          rv = Path.Combine(rv, "SimpleDLNA");
          if (!Directory.Exists(rv)) {
            Directory.CreateDirectory(rv);
          }
          return rv;
        }
        catch (Exception) {
          return Path.GetTempPath();
        }
      }
    }

    public override string Text
    {
      get { return base.Text; }
      set {
        base.Text = value;
        notifyIcon.Text = value;
      }
    }

    private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
    {
      using (var about = new FormAbout()) {
        about.ShowDialog();
      }
    }

    private void ButtonEdit_Click(object sender, EventArgs e)
    {
      var item = listDescriptions.SelectedItems[0] as ServerListViewItem;
      if (item == null) {
        return;
      }
      using (var ns = new FormServer(item.Description)) {
        var rv = ns.ShowDialog();
        if (rv == DialogResult.OK) {
          var desc = ns.Description;
          var id = item.Server.Id;
          Task.Factory.StartNew(() => manager.Update(id, desc));
        }
      }
    }

    private void ButtonNewServer_Click(object sender, EventArgs e)
    {
      using (var ns = new FormServer()) {
        var rv = ns.ShowDialog();
        if (rv == DialogResult.OK) {
          var desc = ns.Description;
          Task.Factory.StartNew(() => manager.Add(desc));
        }
      }
    }

    private void buttonRemove_Click(object sender, EventArgs e)
    {
      var item = listDescriptions.SelectedItems[0] as ServerListViewItem;
      if (item == null) {
        return;
      }
      var dr = MessageBox.Show(
        $"Would you like to remove {item.Description.Name}?",
        "Remove Server",
        MessageBoxButtons.YesNo,
        MessageBoxIcon.Question);
      if (dr != DialogResult.Yes) {
        return;
      }
      var id = item.Server.Id;
      Task.Factory.StartNew(() => manager.Remove(id));
    }

    private void buttonRescan_Click(object sender, EventArgs e)
    {
      try {
        var item = listDescriptions.SelectedItems[0] as ServerListViewItem;
        if (item == null) {
          return;
        }
        item.Server.Rescan();
      }
      catch (Exception ex) {
        MessageBox.Show(
          this, ex.Message, "Error", MessageBoxButtons.OK,
          MessageBoxIcon.Error);
      }
    }

    private void ButtonStartStop_Click(object sender, EventArgs e)
    {
      var item = listDescriptions.SelectedItems[0] as ServerListViewItem;
      if (item == null) {
        return;
      }
      var id = item.Server.Id;
      Task.Factory.StartNew(() =>
      {
        manager.Toggle(id);
        SafeInvoke(() =>
        {
          ctxStartStop.Text = buttonStartStop.Text =
            item.Description.Active ? "Stop" : "Start";
          ctxStartStop.Image = buttonStartStop.Image =
            item.Description.Active
              ? Resources.inactive
              : Resources.active;
        });
      });
    }

    private void dropCacheToolStripMenuItem_Click(object sender, EventArgs e)
    {
      var res = MessageBox.Show(
        this,
        "Are you sure you want to drop the cache?",
        "Drop cache",
        MessageBoxButtons.YesNo,
        MessageBoxIcon.Warning);
      if (res != DialogResult.Yes) {
        return;
      }
      Task.Factory.StartNew(() => manager.DropCache());
    }

    private void exitContextMenuItem_Click(object sender, EventArgs e)
    {
      canClose = true;
      notifyIcon_DoubleClick(sender, e);
      Close();
    }

    private void FormMain_FormClosed(object sender, FormClosedEventArgs e)
    {
      Text = "Going down...";
      httpServer.Playback.Changed -= PlaybackChanged;
      if (adminHost != null) {
        adminHost.Dispose();
        adminHost = null;
      }
      if (settingsStore != null) {
        settingsStore.Changed -= SettingsChanged;
        settingsStore = null;
      }
      if (manager != null) {
        manager.ListChanged -= ManagerListChanged;
        manager.StateChanged -= ManagerStateChanged;
        manager.Dispose();
        manager = null;
      }
      httpServer.Dispose();
      httpServer = null;
      // Releases the wake request; the machine can sleep normally again.
      sleepInhibitor.Dispose();
    }

    private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
    {
      e.Cancel = !canClose;
      if (!canClose) {
        WindowState = FormWindowState.Minimized;
      }
    }

    private void FormMain_Resize(object sender, EventArgs e)
    {
      if (WindowState == FormWindowState.Minimized) {
        ShowInTaskbar = false;
        minimized = true;
        Hide();
      }
    }

    private void hideToolStripMenuItem_Click(object sender, EventArgs e)
    {
      WindowState = FormWindowState.Minimized;
    }

    private void homepageToolStripMenuItem_Click(object sender, EventArgs e)
    {
      Process.Start("http://nmaier.github.io/simpleDLNA/");
    }

    private void listDescriptions_DoubleClick(object sender, EventArgs e)
    {
      if (buttonEdit.Enabled) {
        ButtonEdit_Click(sender, e);
      }
      else {
        ButtonNewServer_Click(sender, e);
      }
    }

    private void ListDescriptions_SelectedIndexChanged(object sender,
      EventArgs e)
    {
      var enable = listDescriptions.SelectedItems.Count != 0;
      ctxStartStop.Enabled = ctxRemove.Enabled = ctxEdit.Enabled =
        buttonStartStop.Enabled = buttonRemove.Enabled = buttonEdit.Enabled =
          enable;
      if (enable) {
        var item = (ServerListViewItem)listDescriptions.SelectedItems[0];
        ctxStartStop.Text = buttonStartStop.Text =
          item.Description.Active ? "Stop" : "Start";
        ctxStartStop.Image = buttonStartStop.Image =
          item.Description.Active
            ? Resources.inactive
            : Resources.active;
        ctxRescan.Enabled = buttonRescan.Enabled = item.Description.Active;
      }
      else {
        ctxRescan.Enabled = buttonRescan.Enabled = false;
      }
    }

    /// <summary>
    ///   Loads and starts servers off the UI thread. Rows appear as soon as the
    ///   descriptions are read; scanning happens behind them.
    /// </summary>
    private void LoadConfig()
    {
      Task.Factory.StartNew(() =>
      {
        try {
          manager.Load(LegacyDescriptors());
        }
        catch (Exception ex) {
          log.Error("Failed to load the server configuration", ex);
          return;
        }
        SafeInvoke(() =>
        {
          try {
            // One-way migration: once descriptors.xml exists, the copy that
            // used to live in user.config is dead weight.
            config.Descriptors?.Clear();
            config.Save();
          }
          catch (Exception) {
            // ignored
          }
        });
      });
    }

    private static System.Collections.Generic.IEnumerable<ServerDescription>
      LegacyDescriptors()
    {
      try {
        return config.Descriptors;
      }
      catch (Exception) {
        return null;
      }
    }

    private void notifyContext_Opening(object sender,
      CancelEventArgs e)
    {
      var items = (from ToolStripItem i in notifyContext.Items
                   where i.Tag != null
                   select i).ToList();
      foreach (var i in items) {
        notifyContext.Items.Remove(i);
      }
      items.Clear();
      if (listDescriptions.Items.Count == 0) {
        ContextSeperatorPre.Visible = false;
        return;
      }
      ContextSeperatorPre.Visible = true;
      foreach (ServerListViewItem item in listDescriptions.Items) {
        if (!item.Description.Active) {
          continue;
        }
        var innerItem = item;
        var menuItem =
          new ToolStripMenuItem($"Rescan {item.Text}")
          {
            Tag = innerItem,
            Image = Resources.refreshing
          };
        menuItem.Click += (s, a) =>
        {
          try {
            innerItem.Server.Rescan();
          }
          catch (Exception) {
            // no op
          }
        };
        items.Add(menuItem);
      }
      items.Reverse();
      var idx = notifyContext.Items.IndexOf(ContextSeperatorPre) + 1;
      foreach (var i in items) {
        notifyContext.Items.Insert(idx, i);
      }
    }

    private void notifyIcon_DoubleClick(object sender, EventArgs e)
    {
      minimized = false;
      Show();
      WindowState = FormWindowState.Normal;
      ShowInTaskbar = true;
    }

    private void openInBrowserToolStripMenuItem_Click(object sender,
      EventArgs e)
    {
      Shell($"http://localhost:{httpServer.RealPort}/");
    }

    private void openAdminUiToolStripMenuItem_Click(object sender, EventArgs e)
    {
      if (adminHost == null) {
        MessageBox.Show(
          this, "The admin interface is not running.", "SimpleDLNA",
          MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return;
      }
      Shell(adminHost.Url);
    }

    private void openLogFolderToolStripMenuItem_Click(object sender,
      EventArgs e)
    {
      Shell(CacheDir);
    }

    /// <summary>
    ///   Hands a URL or path to the shell to open with whatever is registered
    ///   for it.
    /// </summary>
    /// <remarks>
    ///   UseShellExecute must be set explicitly: it defaulted to true on .NET
    ///   Framework but defaults to false on .NET, where passing a URL or a
    ///   directory to Process.Start throws Win32Exception instead of opening
    ///   anything.
    /// </remarks>
    private void Shell(string target)
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
          this, $"Could not open {target}\n\n{ex.Message}", "Error",
          MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
    }

    private void rescanAllContextMenuItem_Click(object sender, EventArgs e)
    {
      Task.Factory.StartNew(() => manager.RescanAll());
    }

    private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
    {
      using (var settings = new FormSettings()) {
        settings.ShowDialog();
        config.Save();
        SetupLogging();
        // Picked up by the next server start, matching the dialog's
        // "(Applies when a server is restarted)" wording.
        manager.Options.ChangeDelay =
          TimeSpan.FromSeconds((double)config.rescandelay);
        manager.Options.RescanInterval =
          TimeSpan.FromMinutes((double)config.rescaninterval);
        // Push the dialog's values into the store the API reads.
        settingsStore?.Save(SettingsFromConfig());
      }
    }

    /// <summary>
    ///   Log levels offered in the settings dialog, coarsest first.
    /// </summary>
    internal static readonly string[] LogLevels =
    {
      "None", "Fatal", "Error", "Warn", "Info", "Debug"
    };

    internal const string DefaultLogLevel = "Error";

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

    /// <summary>
    ///   Logging always goes to sdlna.log in the cache directory. Rolling is
    ///   composite - by date so yesterday's log is separate, and by size so a
    ///   single noisy day cannot fill the disk either. MaxSizeRollBackups keeps
    ///   exactly one rolled file, so at most about a day of history survives.
    /// </summary>
    private void SetupLogging()
    {
      var hierarchy = (Hierarchy)LogManager.GetRepository();
      // Called again whenever the settings dialog closes; without resetting,
      // every visit would stack another appender on the root logger.
      hierarchy.ResetConfiguration();

      var level = ToLog4NetLevel(config.loglevel);
      if (level == Level.Off) {
        hierarchy.Root.Level = Level.Off;
        hierarchy.Threshold = Level.Off;
        hierarchy.Configured = true;
        return;
      }

      var layout = new PatternLayout
      {
        ConversionPattern = "%date %6level [%3thread] %-30.30logger{1} - %message%newline%exception"
      };
      layout.ActivateOptions();
      var fileAppender = new RollingFileAppender
      {
        File = logFile.FullName,
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

    private void SetupServer()
    {
      httpServer = new HttpServer((int)config.port);
      manager = new ServerManager(
        httpServer,
        new DescriptorStore(Path.Combine(CacheDir, DESCRIPTOR_FILE)),
        new ServerManagerOptions
        {
#if DEBUG
          CacheFile = null,
#else
          CacheFile = cacheFile,
#endif
          ChangeDelay = TimeSpan.FromSeconds((double)config.rescandelay),
          RescanInterval =
            TimeSpan.FromMinutes((double)config.rescaninterval)
        });
      manager.ListChanged += ManagerListChanged;
      manager.StateChanged += ManagerStateChanged;
      SetupAdmin();
      LoadConfig();
      Text = $"{Text} - Port {httpServer.RealPort}";
    }

    /// <summary>
    ///   Starts the loopback admin API.
    /// </summary>
    /// <remarks>
    ///   While both UIs exist, user.config stays the dialog's editing surface
    ///   and settings.json is what the API reads and writes; the two are synced
    ///   at the few points where either can change. All of this goes away with
    ///   the forms.
    /// </remarks>
    private void SetupAdmin()
    {
      try {
        settingsStore = new SettingsStore(Paths.SettingsFile);
        settingsStore.SeedIfMissing(SettingsFromConfig());
        settingsStore.Changed += SettingsChanged;

        adminHost = new AdminHost(new AdminContext
        {
          Http = httpServer,
          Manager = manager,
          Settings = settingsStore,
          Managed = true,
          HostKind = "tray",
          GetAutostart = GetAutostart,
          SetAutostart = SetAutostart
        });
        log.InfoFormat("Admin UI available at {0}", adminHost.Url);
      }
      catch (AdminServerBindException ex) {
        log.Error("Could not start the admin interface", ex);
        MessageBox.Show(
          this, ex.Message, "SimpleDLNA", MessageBoxButtons.OK,
          MessageBoxIcon.Warning);
      }
      catch (Exception ex) {
        log.Error("Could not start the admin interface", ex);
      }
    }

    private static AppSettings SettingsFromConfig()
    {
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

    /// <summary>
    ///   Mirrors a change made through the API back into the dialog's settings
    ///   and applies whatever takes effect immediately.
    /// </summary>
    private void SettingsChanged(object sender, SettingsChangedEventArgs e)
    {
      var s = e.Current;
      SafeInvoke(() =>
      {
        config.port = s.Port;
        config.cache = s.CacheDir ?? string.Empty;
        config.rescandelay = s.RescanDelaySeconds;
        config.rescaninterval = s.RescanIntervalMinutes;
        config.loglevel = s.LogLevel;
        config.startminimized = s.StartMinimized;
        config.preventsleep = s.PreventSleep;
        config.Save();

        preventSleepToolStripMenuItem.Checked = s.PreventSleep;
        SetupLogging();
        UpdatePlaybackState();
        if (manager != null) {
          manager.Options.ChangeDelay =
            TimeSpan.FromSeconds(s.RescanDelaySeconds);
          manager.Options.RescanInterval =
            TimeSpan.FromMinutes(s.RescanIntervalMinutes);
        }
      });
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

    /// <summary>
    ///   Runs an action on the UI thread, tolerating a shutdown race.
    /// </summary>
    private void SafeInvoke(Action action)
    {
      if (!IsHandleCreated || IsDisposed) {
        return;
      }
      try {
        BeginInvoke(action);
      }
      catch (ObjectDisposedException) {
      }
      catch (InvalidOperationException) {
      }
    }

    private void ManagerListChanged(object sender, EventArgs e)
    {
      SafeInvoke(RebuildList);
    }

    private void ManagerStateChanged(object sender,
      ServerStateChangedEventArgs e)
    {
      SafeInvoke(() =>
      {
        var item = (from ServerListViewItem i in listDescriptions.Items
                    where ReferenceEquals(i.Server, e.Server)
                    select i).FirstOrDefault();
        if (item == null) {
          return;
        }
        item.Render();
        AutoSizeColumns();
        ListDescriptions_SelectedIndexChanged(null, EventArgs.Empty);
      });
    }

    private void RebuildList()
    {
      var selected =
        (listDescriptions.SelectedItems.Count != 0
          ? listDescriptions.SelectedItems[0] as ServerListViewItem
          : null)?.Server;

      listDescriptions.BeginUpdate();
      try {
        listDescriptions.Items.Clear();
        foreach (var s in manager.Servers) {
          var item = new ServerListViewItem(s);
          listDescriptions.Items.Add(item);
          if (ReferenceEquals(s, selected)) {
            item.Selected = true;
          }
        }
      }
      finally {
        listDescriptions.EndUpdate();
      }
      AutoSizeColumns();
      ListDescriptions_SelectedIndexChanged(null, EventArgs.Empty);
    }

    private void AutoSizeColumns()
    {
      var mode = listDescriptions.Items.Count == 0
        ? ColumnHeaderAutoResizeStyle.HeaderSize
        : ColumnHeaderAutoResizeStyle.ColumnContent;
      foreach (var c in listDescriptions.Columns) {
        ((ColumnHeader)c).AutoResize(mode);
      }
    }

    private void StartPipeNotification()
    {
#if DEBUG
      log.Info("Debug mode / Skipping one-instance-only stuff");
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
              "simpledlnagui", PipeDirection.InOut)) {
              pipe.WaitForConnection();
              pipe.ReadByte();
              BeginInvoke((Action)(() =>
              {
                notifyIcon_DoubleClick(null, null);
                BringToFront();
              }));
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

    protected override void SetVisibleCore(bool value)
    {
      if (minimized) {
        value = false;
        if (!IsHandleCreated) {
          CreateHandle();
        }
      }
      notifyIcon.Visible = !value;
      base.SetVisibleCore(value);
    }

  }
}

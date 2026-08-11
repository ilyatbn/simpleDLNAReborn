using System;
using System.IO;
using System.Text.Json;
using log4net;

namespace NMaier.SimpleDlna.Admin
{
  /// <summary>
  ///   Loads and saves <see cref="AppSettings" />, and reports what changed.
  /// </summary>
  public sealed class SettingsStore
  {
    private static readonly ILog log =
      LogManager.GetLogger(typeof (SettingsStore));

    private static readonly JsonSerializerOptions options =
      new JsonSerializerOptions
      {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
      };

    private readonly object sync = new object();

    private AppSettings current;

    public SettingsStore(string path)
    {
      Path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public string Path { get; }

    public bool Exists => File.Exists(Path);

    public AppSettings Current
    {
      get {
        lock (sync) {
          return (current ?? (current = Load())).Clone();
        }
      }
    }

    /// <summary>Raised after a successful save.</summary>
    public event EventHandler<SettingsChangedEventArgs> Changed;

    public AppSettings Load()
    {
      lock (sync) {
        AppSettings rv = null;
        try {
          if (File.Exists(Path)) {
            var json = File.ReadAllText(Path);
            rv = JsonSerializer.Deserialize<AppSettings>(json, options);
          }
        }
        catch (Exception ex) {
          log.Error($"Failed to read {Path}; using defaults", ex);
        }
        rv = rv ?? new AppSettings();
        rv.Clamp();
        current = rv;
        return rv.Clone();
      }
    }

    public void Save(AppSettings settings)
    {
      if (settings == null) {
        throw new ArgumentNullException(nameof(settings));
      }
      settings.Clamp();
      AppSettings previous;
      lock (sync) {
        previous = current ?? new AppSettings();
        try {
          var dir = System.IO.Path.GetDirectoryName(Path);
          if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
            Directory.CreateDirectory(dir);
          }
          var tmp = Path + ".tmp";
          File.WriteAllText(tmp, JsonSerializer.Serialize(settings, options));
          File.Copy(tmp, Path, true);
          File.Delete(tmp);
        }
        catch (Exception ex) {
          log.Error($"Failed to write {Path}", ex);
        }
        current = settings.Clone();
      }
      try {
        Changed?.Invoke(
          this, new SettingsChangedEventArgs(previous, settings.Clone()));
      }
      catch (Exception ex) {
        log.Error("A settings listener failed", ex);
      }
    }

    /// <summary>
    ///   Writes seed values only when no settings file exists yet, which is how
    ///   the tray host hands over what used to live in user.config.
    /// </summary>
    public bool SeedIfMissing(AppSettings seed)
    {
      if (seed == null || Exists) {
        return false;
      }
      log.Info("Migrating settings from the legacy user configuration");
      Save(seed);
      return true;
    }
  }

  public sealed class SettingsChangedEventArgs : EventArgs
  {
    internal SettingsChangedEventArgs(AppSettings previous, AppSettings current)
    {
      Previous = previous;
      Current = current;
    }

    public AppSettings Previous { get; }

    public AppSettings Current { get; }

    /// <summary>
    ///   Settings that only take effect after a restart, because the HTTP
    ///   listener is created once and never rebound.
    /// </summary>
    public string[] RestartRequired()
    {
      var rv = new System.Collections.Generic.List<string>();
      if (Previous.Port != Current.Port) {
        rv.Add("port");
      }
      if (!string.Equals(Previous.CacheDir ?? string.Empty,
        Current.CacheDir ?? string.Empty, StringComparison.OrdinalIgnoreCase)) {
        rv.Add("cacheDir");
      }
      return rv.ToArray();
    }
  }
}

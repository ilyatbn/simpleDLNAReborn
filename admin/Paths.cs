using System;
using System.IO;

namespace NMaier.SimpleDlna.Admin
{
  /// <summary>
  ///   Where configuration, cache and logs live.
  /// </summary>
  /// <remarks>
  ///   Configuration deliberately does NOT follow the configurable cache
  ///   directory. It used to: descriptors.xml was written under the directory
  ///   the "cache" setting pointed at, so changing that setting made every
  ///   configured server vanish. settings.json holds the cache directory
  ///   itself, which makes the circularity obvious. Configuration is not cache.
  /// </remarks>
  public static class Paths
  {
    public const string SettingsFileName = "settings.json";

    public const string DescriptorsFileName = "descriptors.xml";

    public const string CacheFileName = "sdlna.cache";

    public const string LogFileName = "sdlna.log";

    /// <summary>
    ///   %LOCALAPPDATA%\SimpleDLNA, falling back to %APPDATA% and then TEMP.
    ///   Always the same directory regardless of settings.
    /// </summary>
    public static string DataDir
    {
      get {
        string rv;
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
              throw new IOException("Cannot get AppData");
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

    public static string SettingsFile => Path.Combine(DataDir, SettingsFileName);

    public static string DescriptorsFile =>
      Path.Combine(DataDir, DescriptorsFileName);

    /// <summary>
    ///   The directory holding the metadata cache and the log. Falls back to
    ///   <see cref="DataDir" /> when unset or missing.
    /// </summary>
    public static string ResolveCacheDir(string configured)
    {
      if (string.IsNullOrWhiteSpace(configured)) {
        return DataDir;
      }
      try {
        // A value pointing at a file is normalised to its parent: the setting
        // used to be read as a file path in one place and a directory in
        // another, so both shapes exist in the wild.
        if (File.Exists(configured)) {
          var parent = Path.GetDirectoryName(configured);
          if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) {
            return parent;
          }
          return DataDir;
        }
        if (!Directory.Exists(configured)) {
          Directory.CreateDirectory(configured);
        }
        return configured;
      }
      catch (Exception) {
        return DataDir;
      }
    }

    public static FileInfo CacheFile(string cacheDir)
    {
      return new FileInfo(Path.Combine(cacheDir, CacheFileName));
    }

    public static FileInfo LogFile(string cacheDir)
    {
      return new FileInfo(Path.Combine(cacheDir, LogFileName));
    }
  }
}

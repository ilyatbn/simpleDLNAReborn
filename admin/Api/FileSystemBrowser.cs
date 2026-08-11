using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NMaier.SimpleDlna.Admin.Api
{
  /// <summary>
  ///   Directory listing for the folder picker.
  /// </summary>
  /// <remarks>
  ///   Required, not optional: a browser cannot open FolderBrowserDialog, so
  ///   without this there is no way to choose a media directory. It lists
  ///   directories only - the picker never chooses files - and it is one of the
  ///   reasons the whole API is loopback-only.
  /// </remarks>
  public static class FileSystemBrowser
  {
    public static FsDto List(string path)
    {
      if (string.IsNullOrWhiteSpace(path)) {
        return Roots();
      }
      var dir = new DirectoryInfo(path);
      if (!dir.Exists) {
        return null;
      }
      var rv = new FsDto
      {
        Path = dir.FullName,
        Parent = dir.Parent?.FullName,
        Entries = new List<FsEntryDto>()
      };
      // A drive root has no Parent but should still navigate back to the drive
      // list, which the SPA models as a null path.
      DirectoryInfo[] children;
      try {
        children = dir.GetDirectories();
      }
      catch (UnauthorizedAccessException) {
        return rv;
      }
      catch (IOException) {
        return rv;
      }
      foreach (var c in children.OrderBy(c => c.Name,
        StringComparer.CurrentCultureIgnoreCase)) {
        if ((c.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden) {
          continue;
        }
        rv.Entries.Add(new FsEntryDto
        {
          Name = c.Name,
          Path = c.FullName,
          Accessible = IsAccessible(c),
          HasChildren = HasChildren(c)
        });
      }
      return rv;
    }

    private static FsDto Roots()
    {
      var rv = new FsDto {Path = null, Parent = null, Entries = new List<FsEntryDto>()};
      DriveInfo[] drives;
      try {
        drives = DriveInfo.GetDrives();
      }
      catch (Exception) {
        return rv;
      }
      foreach (var d in drives) {
        var ready = false;
        try {
          ready = d.IsReady;
        }
        catch (Exception) {
          // ignored
        }
        if (!ready) {
          continue;
        }
        var name = d.Name;
        try {
          if (!string.IsNullOrWhiteSpace(d.VolumeLabel)) {
            name = $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})";
          }
        }
        catch (Exception) {
          // ignored
        }
        rv.Entries.Add(new FsEntryDto
        {
          Name = name,
          Path = d.RootDirectory.FullName,
          Accessible = true,
          HasChildren = true
        });
      }
      return rv;
    }

    private static bool IsAccessible(DirectoryInfo dir)
    {
      try {
        dir.EnumerateDirectories().FirstOrDefault();
        return true;
      }
      catch (UnauthorizedAccessException) {
        return false;
      }
      catch (IOException) {
        return false;
      }
      catch (Exception) {
        return false;
      }
    }

    private static bool HasChildren(DirectoryInfo dir)
    {
      try {
        return dir.EnumerateDirectories().Any();
      }
      catch (Exception) {
        return false;
      }
    }
  }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using log4net;

namespace NMaier.SimpleDlna.Admin
{
  /// <summary>
  ///   Reads and writes descriptors.xml, the source of truth for configured
  ///   servers.
  /// </summary>
  /// <remarks>
  ///   Format is unchanged from the WinForms GUI - an XmlSerializer over an
  ///   array of <see cref="ServerDescription" /> - so an existing file keeps
  ///   working. Writes go to a temporary file first, because a half-written
  ///   descriptors.xml loses every configured server.
  /// </remarks>
  public sealed class DescriptorStore
  {
    private static readonly ILog log =
      LogManager.GetLogger(typeof (DescriptorStore));

    private readonly object sync = new object();

    public DescriptorStore(string path)
    {
      if (string.IsNullOrWhiteSpace(path)) {
        throw new ArgumentNullException(nameof(path));
      }
      Path = path;
    }

    public string Path { get; }

    /// <summary>
    ///   Returns the stored descriptions, or null when the file is missing or
    ///   unreadable - which the caller distinguishes from "no servers".
    /// </summary>
    public List<ServerDescription> Load()
    {
      lock (sync) {
        if (!File.Exists(Path)) {
          return null;
        }
        try {
          var serializer =
            new XmlSerializer(typeof (List<ServerDescription>));
          using (var reader = new StreamReader(Path)) {
            return serializer.Deserialize(reader) as List<ServerDescription>;
          }
        }
        catch (Exception ex) {
          log.Error($"Failed to read {Path}", ex);
          return null;
        }
      }
    }

    public void Save(IEnumerable<ServerDescription> descriptions)
    {
      if (descriptions == null) {
        throw new ArgumentNullException(nameof(descriptions));
      }
      var list = new List<ServerDescription>(descriptions);
      lock (sync) {
        try {
          var dir = System.IO.Path.GetDirectoryName(Path);
          if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
            Directory.CreateDirectory(dir);
          }
          var tmp = Path + ".tmp";
          var serializer =
            new XmlSerializer(typeof (List<ServerDescription>));
          using (var writer = new StreamWriter(tmp)) {
            serializer.Serialize(writer, list);
          }
          File.Copy(tmp, Path, true);
          File.Delete(tmp);
        }
        catch (Exception ex) {
          log.Error("Failed to write descriptors", ex);
        }
      }
    }
  }
}

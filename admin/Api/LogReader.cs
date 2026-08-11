using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace NMaier.SimpleDlna.Admin.Api
{
  /// <summary>
  ///   Reads the tail of sdlna.log.
  /// </summary>
  /// <remarks>
  ///   Replaces the GUI's "Open Log Folder", which a browser cannot do. The
  ///   file must be opened shared for read AND write: log4net holds it open
  ///   with ImmediateFlush.
  /// </remarks>
  public static class LogReader
  {
    private const int MAX_TAIL = 5000;

    // "%date %6level [%3thread] %-30.30logger{1} - %message"
    private static readonly Regex line = new Regex(
      @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3})\s+" +
      @"(?<level>[A-Z]+)\s+\[\s*(?<thread>[^\]]*)\]\s+" +
      @"(?<logger>\S+)\s+-\s(?<message>.*)$",
      RegexOptions.Compiled);

    public static LogDto Read(FileInfo file, int tail, string minimumLevel)
    {
      tail = Math.Max(1, Math.Min(MAX_TAIL, tail));
      var rv = new LogDto
      {
        Path = file?.FullName,
        Level = minimumLevel,
        Lines = new List<LogLineDto>()
      };
      if (file == null) {
        rv.Disabled = true;
        return rv;
      }
      file.Refresh();
      if (!file.Exists) {
        rv.Disabled = true;
        return rv;
      }
      rv.TotalBytes = file.Length;

      var raw = ReadLastLines(file, tail);
      var threshold = LevelRank(minimumLevel);
      foreach (var text in raw) {
        var m = line.Match(text);
        if (!m.Success) {
          // Continuation lines - stack traces - are kept verbatim so an
          // exception stays readable.
          rv.Lines.Add(new LogLineDto {Message = text});
          continue;
        }
        var level = m.Groups["level"].Value;
        if (threshold > 0 && LevelRank(level) < threshold) {
          continue;
        }
        rv.Lines.Add(new LogLineDto
        {
          Timestamp = m.Groups["ts"].Value,
          Level = level,
          Logger = m.Groups["logger"].Value,
          Message = m.Groups["message"].Value
        });
      }
      return rv;
    }

    /// <summary>
    ///   Reads the last <paramref name="count" /> lines without loading the
    ///   whole file, which matters because the log rolls at 5 MB.
    /// </summary>
    private static List<string> ReadLastLines(FileInfo file, int count)
    {
      var rv = new List<string>();
      try {
        using (var stream = new FileStream(
          file.FullName, FileMode.Open, FileAccess.Read,
          FileShare.ReadWrite | FileShare.Delete)) {
          const int chunkSize = 32 * 1024;
          var length = stream.Length;
          var position = length;
          var pending = new List<byte>();
          var lines = new LinkedList<string>();

          while (position > 0 && lines.Count <= count) {
            var size = (int)Math.Min(chunkSize, position);
            position -= size;
            stream.Seek(position, SeekOrigin.Begin);
            var chunk = new byte[size];
            var read = stream.Read(chunk, 0, size);
            if (read <= 0) {
              break;
            }
            for (var i = read - 1; i >= 0; --i) {
              if (chunk[i] == '\n') {
                pending.Reverse();
                lines.AddFirst(
                  Encoding.UTF8.GetString(pending.ToArray()).TrimEnd('\r'));
                pending.Clear();
                if (lines.Count > count) {
                  break;
                }
                continue;
              }
              pending.Add(chunk[i]);
            }
          }
          if (pending.Count != 0 && lines.Count <= count) {
            pending.Reverse();
            lines.AddFirst(
              Encoding.UTF8.GetString(pending.ToArray()).TrimEnd('\r'));
          }
          foreach (var l in lines) {
            if (l.Length != 0) {
              rv.Add(l);
            }
          }
        }
      }
      catch (Exception) {
        // A locked or vanished log is not worth failing the request over.
      }
      if (rv.Count > count) {
        rv.RemoveRange(0, rv.Count - count);
      }
      return rv;
    }

    private static int LevelRank(string level)
    {
      switch ((level ?? string.Empty).ToUpperInvariant()) {
      case "DEBUG": return 1;
      case "INFO": return 2;
      case "NOTICE": return 3;
      case "WARN": return 4;
      case "ERROR": return 5;
      case "FATAL": return 6;
      default: return 0;
      }
    }
  }
}

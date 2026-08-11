using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NMaier.SimpleDlna.Server;
using NMaier.SimpleDlna.Server.Comparers;
using NMaier.SimpleDlna.Server.Views;
using NMaier.SimpleDlna.Utilities;

namespace NMaier.SimpleDlna.Admin.Api
{
  /// <summary>
  ///   Server-side validation of a server description.
  /// </summary>
  /// <remarks>
  ///   The messages are the WinForms validator strings verbatim, so the web UI
  ///   can show exactly what the old dialog showed.
  /// </remarks>
  internal static class Validation
  {
    public static List<FieldError> Validate(ServerInputDto input)
    {
      var rv = new List<FieldError>();
      if (input == null) {
        rv.Add(new FieldError("name", "Must specify a name"));
        return rv;
      }

      if (string.IsNullOrWhiteSpace(input.Name)) {
        rv.Add(new FieldError("name", "Must specify a name"));
      }

      var types = input.Types ?? new string[0];
      if (types.Length == 0) {
        rv.Add(new FieldError("types", "Must select at least one"));
      }
      else {
        foreach (var t in types) {
          if (ParseType(t) == 0) {
            rv.Add(new FieldError("types", $"Unknown media type '{t}'"));
          }
        }
      }

      var dirs = (input.Directories ?? new string[0])
        .Where(d => !string.IsNullOrWhiteSpace(d)).ToArray();
      if (dirs.Length == 0) {
        rv.Add(new FieldError(
          "directories", "Must specify at least one directory"));
      }

      if (string.IsNullOrWhiteSpace(input.Order)) {
        rv.Add(new FieldError("order", "Must specify a sort order"));
      }
      else {
        try {
          ComparerRepository.Lookup(input.Order);
        }
        catch (Exception) {
          rv.Add(new FieldError(
            "order", $"Unknown sort order '{input.Order}'"));
        }
      }

      var views = input.Views ?? new string[0];
      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var v in views) {
        if (string.IsNullOrWhiteSpace(v)) {
          continue;
        }
        if (!seen.Add(v)) {
          rv.Add(new FieldError("views", $"Duplicate view '{v}'"));
          continue;
        }
        try {
          ViewRepository.Lookup(v);
        }
        catch (Exception) {
          rv.Add(new FieldError("views", $"Unknown view '{v}'"));
        }
      }

      var r = input.Restrictions ?? new RestrictionsDto();
      foreach (var mac in r.Mac ?? new string[0]) {
        if (!IP.IsAcceptedMAC(mac)) {
          rv.Add(new FieldError(
            "restrictions.mac", "You must provide a valid value"));
        }
      }
      foreach (var ip in r.Ip ?? new string[0]) {
        IPAddress parsed;
        if (!IPAddress.TryParse(ip, out parsed)) {
          rv.Add(new FieldError(
            "restrictions.ip", "You must provide a valid value"));
        }
      }
      foreach (var ua in r.UserAgent ?? new string[0]) {
        if (string.IsNullOrWhiteSpace(ua)) {
          rv.Add(new FieldError(
            "restrictions.userAgent", "You must provide a valid value"));
        }
      }

      return rv;
    }

    /// <summary>
    ///   Copies validated input onto a description. Never touches Active or Id.
    /// </summary>
    public static ServerDescription ToDescription(ServerInputDto input,
      ServerDescription target)
    {
      var r = input.Restrictions ?? new RestrictionsDto();
      target.Name = (input.Name ?? string.Empty).Trim();
      target.Order = input.Order;
      target.OrderDescending = input.OrderDescending;
      target.Types = ParseTypes(input.Types);
      target.Views = Clean(input.Views);
      target.Directories = Clean(input.Directories);
      target.Macs = Clean(r.Mac);
      target.Ips = Clean(r.Ip);
      target.UserAgents = Clean(r.UserAgent);
      return target;
    }

    public static string[] TypesToArray(DlnaMediaTypes types)
    {
      var rv = new List<string>();
      if ((types & DlnaMediaTypes.Video) == DlnaMediaTypes.Video) {
        rv.Add("video");
      }
      if ((types & DlnaMediaTypes.Audio) == DlnaMediaTypes.Audio) {
        rv.Add("audio");
      }
      if ((types & DlnaMediaTypes.Image) == DlnaMediaTypes.Image) {
        rv.Add("image");
      }
      return rv.ToArray();
    }

    public static DlnaMediaTypes ParseTypes(string[] types)
    {
      DlnaMediaTypes rv = 0;
      foreach (var t in types ?? new string[0]) {
        rv |= ParseType(t);
      }
      return rv;
    }

    private static DlnaMediaTypes ParseType(string type)
    {
      switch ((type ?? string.Empty).Trim().ToLowerInvariant()) {
      case "video": return DlnaMediaTypes.Video;
      case "audio": return DlnaMediaTypes.Audio;
      case "image":
      case "images":
      case "picture": return DlnaMediaTypes.Image;
      default: return 0;
      }
    }

    private static string[] Clean(string[] values)
    {
      return (values ?? new string[0])
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Select(v => v.Trim())
        .ToArray();
    }
  }
}

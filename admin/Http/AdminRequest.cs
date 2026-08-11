using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace NMaier.SimpleDlna.Admin.Http
{
  /// <summary>
  ///   One parsed HTTP request.
  /// </summary>
  /// <remarks>
  ///   Deliberately not the DLNA server's request type. That one never splits
  ///   the query string off the path, never URL-decodes, and round-trips the
  ///   body through <c>Encoding.ASCII</c>, which turns every non-ASCII
  ///   character into '?'. An admin API that accepts folder paths and server
  ///   names cannot live with any of those.
  /// </remarks>
  public sealed class AdminRequest
  {
    internal AdminRequest(string method, string rawTarget,
      IDictionary<string, string> headers, byte[] body,
      IPEndPoint remoteEndPoint)
    {
      Method = method;
      RawTarget = rawTarget;
      Headers = headers;
      BodyBytes = body ?? new byte[0];
      RemoteEndPoint = remoteEndPoint;

      var split = rawTarget.IndexOf('?');
      var rawPath = split < 0 ? rawTarget : rawTarget.Substring(0, split);
      Path = Decode(rawPath);
      Query = split < 0
        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        : ParseQuery(rawTarget.Substring(split + 1));
    }

    public string Method { get; }

    /// <summary>The request target exactly as it came in.</summary>
    public string RawTarget { get; }

    /// <summary>Percent-decoded path, without the query string.</summary>
    public string Path { get; }

    public IDictionary<string, string> Query { get; }

    /// <summary>Header names are lowercased.</summary>
    public IDictionary<string, string> Headers { get; }

    public byte[] BodyBytes { get; }

    public IPEndPoint RemoteEndPoint { get; }

    /// <summary>The body decoded as UTF-8, losslessly.</summary>
    public string Body => Encoding.UTF8.GetString(BodyBytes);

    public string Header(string name)
    {
      string rv;
      return Headers.TryGetValue(name, out rv) ? rv : null;
    }

    public string QueryValue(string name)
    {
      string rv;
      return Query.TryGetValue(name, out rv) ? rv : null;
    }

    public int QueryInt(string name, int fallback)
    {
      int rv;
      return int.TryParse(QueryValue(name), out rv) ? rv : fallback;
    }

    public bool WantsKeepAlive()
    {
      var conn = Header("connection");
      return conn == null ||
             !conn.Equals("close", StringComparison.OrdinalIgnoreCase);
    }

    private static IDictionary<string, string> ParseQuery(string query)
    {
      var rv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      foreach (var pair in query.Split('&')) {
        if (pair.Length == 0) {
          continue;
        }
        var eq = pair.IndexOf('=');
        if (eq < 0) {
          rv[Decode(pair)] = string.Empty;
          continue;
        }
        rv[Decode(pair.Substring(0, eq))] = Decode(pair.Substring(eq + 1));
      }
      return rv;
    }

    /// <summary>
    ///   Percent-decoding that also turns '+' into a space, which is what
    ///   browsers send in query strings.
    /// </summary>
    private static string Decode(string value)
    {
      if (string.IsNullOrEmpty(value)) {
        return value ?? string.Empty;
      }
      try {
        return Uri.UnescapeDataString(value.Replace("+", "%20"));
      }
      catch (Exception) {
        return value;
      }
    }
  }
}

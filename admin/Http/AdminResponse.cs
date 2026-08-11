using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NMaier.SimpleDlna.Admin.Http
{
  /// <summary>
  ///   One HTTP response: either a complete byte body, or an open-ended stream
  ///   for Server-Sent Events.
  /// </summary>
  public sealed class AdminResponse
  {
    public AdminResponse(int status, string contentType, byte[] body)
    {
      Status = status;
      Body = body ?? new byte[0];
      if (!string.IsNullOrEmpty(contentType)) {
        Headers["Content-Type"] = contentType;
      }
    }

    public int Status { get; }

    public byte[] Body { get; }

    public IDictionary<string, string> Headers { get; } =
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///   Set for streaming responses. The connection writes headers without a
    ///   Content-Length, hands the stream over, and closes afterwards.
    /// </summary>
    public Action<Stream> StreamBody { get; set; }

    public static AdminResponse Text(int status, string body)
    {
      return new AdminResponse(
        status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(body));
    }

    public static AdminResponse Json(int status, string json)
    {
      return new AdminResponse(
        status, "application/json; charset=utf-8",
        Encoding.UTF8.GetBytes(json));
    }

    public static AdminResponse Empty(int status)
    {
      return new AdminResponse(status, null, null);
    }

    public static AdminResponse Stream(string contentType, Action<Stream> body)
    {
      return new AdminResponse(200, contentType, null) {StreamBody = body};
    }

    /// <summary>
    ///   Reason phrases for every status this API can return. The DLNA server's
    ///   equivalent table throws on anything it does not know; this one falls
    ///   back.
    /// </summary>
    internal static string Phrase(int status)
    {
      switch (status) {
      case 200: return "OK";
      case 201: return "Created";
      case 202: return "Accepted";
      case 204: return "No Content";
      case 304: return "Not Modified";
      case 400: return "Bad Request";
      case 403: return "Forbidden";
      case 404: return "Not Found";
      case 405: return "Method Not Allowed";
      case 409: return "Conflict";
      case 413: return "Payload Too Large";
      case 415: return "Unsupported Media Type";
      case 422: return "Unprocessable Entity";
      case 500: return "Internal Server Error";
      case 503: return "Service Unavailable";
      default: return "Status " + status;
      }
    }
  }
}

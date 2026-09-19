using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using NMaier.SimpleDlna.Utilities;

namespace NMaier.SimpleDlna.Server
{
  internal sealed class HttpClient : Logging, IRequest, IDisposable
  {
    private const uint BEGIN_TIMEOUT = 30;

    private const int BUFFER_SIZE = 1 << 16;

    private const string CRLF = "\r\n";

    /// <summary>Group 1 = suffix form, 2 and 3 = start and optional end.</summary>
    private static readonly Regex bytes =
      new Regex(@"^bytes=(?:-(\d+)|(\d+)-(\d*))$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>A fresh error response; never shared between connections.</summary>
    private static IResponse Error(HttpCode code)
    {
      switch (code) {
      case HttpCode.Denied:
        return new StringResponse(
          HttpCode.Denied,
          "<!doctype html><title>Access denied!</title><h1>Access denied!</h1><p>You're not allowed to access the requested resource.</p>"
          );
      case HttpCode.RangeNotSatisfiable:
        return new StringResponse(
          HttpCode.RangeNotSatisfiable,
          "<!doctype html><title>Requested Range not satisfiable!</title><h1>Requested Range not satisfiable!</h1><p>Nice try, but do not try again :p</p>"
          );
      case HttpCode.InternalError:
        return new StringResponse(
          HttpCode.InternalError,
          "<!doctype html><title>Internal Server Error</title><h1>Internal Server Error</h1><p>Something is very rotten in the State of Denmark!</p>"
          );
      default:
        return new StringResponse(
          HttpCode.NotFound,
          "<!doctype html><title>Not found!</title><h1>Not found!</h1><p>The requested resource was not found!</p>"
          );
      }
    }

    private readonly byte[] buffer = new byte[2048];

    private readonly TcpClient client;

    private readonly HttpServer owner;

    private readonly uint readTimeout =
      (uint)TimeSpan.FromMinutes(1).TotalSeconds;

    private readonly NetworkStream stream;

    private readonly uint writeTimeout =
      (uint)TimeSpan.FromMinutes(180).TotalSeconds;

    private uint bodyBytes;

    private bool hasHeaders;

    /// <summary>Client sent bytes past this request; the connection cannot be reused.</summary>
    private bool pipelined;

    private DateTime lastActivity;

    private MemoryStream readStream;

    private uint requestCount;

    private IResponse response;

    /// <summary>Version off the request line; decides the default persistence.</summary>
    private string protocol = "HTTP/1.1";

    private HttpStates state;

    public HttpClient(HttpServer aOwner, TcpClient aClient)
    {
      State = HttpStates.Accepted;
      lastActivity = DateTime.Now;

      owner = aOwner;
      client = aClient;
      stream = client.GetStream();
      client.Client.UseOnlyOverlappedIO = true;
      try {
        client.NoDelay = true;
      }
      catch (Exception ex) {
        Debug("Could not disable Nagle", ex);
      }

      RemoteEndpoint = client.Client.RemoteEndPoint as IPEndPoint;
      LocalEndPoint = client.Client.LocalEndPoint as IPEndPoint;
    }

    private HttpStates State
    {
      set {
        lastActivity = DateTime.Now;
        state = value;
      }
    }

    public bool IsATimeout
    {
      get {
        var diff = (DateTime.Now - lastActivity).TotalSeconds;
        switch (state) {
        case HttpStates.Accepted:
        case HttpStates.ReadBegin:
        case HttpStates.WriteBegin:
          return diff > BEGIN_TIMEOUT;
        case HttpStates.Reading:
          return diff > readTimeout;
        case HttpStates.Writing:
          return diff > writeTimeout;
        case HttpStates.Closed:
          return true;
        default:
          throw new InvalidOperationException("Invalid state");
        }
      }
    }

    public void Dispose()
    {
      Close();
      readStream?.Dispose();
    }

    public string Body { get; private set; }

    public IHeaders Headers { get; } = new Headers();

    public IPEndPoint LocalEndPoint { get; }

    public string Method { get; private set; }

    public string Path { get; private set; }

    public IPEndPoint RemoteEndpoint { get; }

    private static long GetContentLengthFromStream(IHeaders headers,
      Stream responseBody)
    {
      long contentLength = -1;
      try {
        string clf;
        if (!headers.TryGetValue("Content-Length", out clf) ||
            !long.TryParse(clf, out contentLength)) {
          contentLength = responseBody.Length - responseBody.Position;
          if (contentLength < 0) {
            throw new InvalidDataException();
          }
          headers["Content-Length"] = contentLength.ToString();
        }
      }
      catch (Exception) {
        // ignored
      }
      return contentLength;
    }

    /// <summary>
    ///   Applies the request Range to the response and returns the body the
    ///   client should receive, capped to Content-Length. Writes only into
    ///   <paramref name="headers" />, never the response, which may be shared.
    /// </summary>
    private Stream ProcessRanges(IHeaders headers, ref HttpCode status)
    {
      var responseBody = response.Body;
      var totalLength = GetContentLengthFromStream(headers, responseBody);

      string requested;
      if (!Headers.TryGetValue("Range", out requested)) {
        return responseBody;
      }
      if (status != HttpCode.Ok || totalLength < 0 || !responseBody.CanSeek) {
        return responseBody;
      }

      try {
        var m = bytes.Match(requested.Trim());
        if (!m.Success) {
          DebugFormat("{0} - Ignoring unparsable range {1}", this, requested);
          return responseBody;
        }

        long start;
        long end;
        var suffix = m.Groups[1].Value;
        if (suffix.Length != 0) {
          long fromEnd;
          if (!long.TryParse(suffix, out fromEnd) || fromEnd <= 0) {
            return Unsatisfiable(responseBody, totalLength, ref status);
          }
          start = Math.Max(0, totalLength - fromEnd);
          end = totalLength - 1;
        }
        else {
          if (!long.TryParse(m.Groups[2].Value, out start) || start < 0) {
            return Unsatisfiable(responseBody, totalLength, ref status);
          }
          var last = m.Groups[3].Value;
          if (last.Length == 0 || !long.TryParse(last, out end)) {
            // "bytes=N-": everything from N on.
            end = totalLength - 1;
          }
          else if (end >= totalLength) {
              end = totalLength - 1;
          }
        }

        if (start >= totalLength || end < start) {
          return Unsatisfiable(responseBody, totalLength, ref status);
        }

        responseBody.Seek(start, SeekOrigin.Begin);

        var contentLength = end - start + 1;
        headers["Content-Length"] = contentLength.ToString();
        headers["Content-Range"] = $"bytes {start}-{end}/{totalLength}";
        status = HttpCode.Partial;
        return new LimitedStream(responseBody, contentLength);
      }
      catch (Exception ex) {
        Warn($"{this} - Failed to process range request!", ex);
        return responseBody;
      }
    }

    /// <summary>Swaps in a complete 416: status, body and Content-Range.</summary>
    private Stream Unsatisfiable(Stream responseBody, long totalLength,
      ref HttpCode status)
    {
      DebugFormat(
        "{0} - Unsatisfiable range over {1} bytes", this, totalLength);
      responseBody.Close();
      responseBody.Dispose();
      response = Error(HttpCode.RangeNotSatisfiable);
      response.Headers["Content-Range"] = $"bytes */{totalLength}";
      status = response.Status;
      return response.Body;
    }

    private void Read()
    {
      try {
        stream.BeginRead(buffer, 0, buffer.Length, ReadCallback, 0);
      }
      catch (IOException ex) {
        Warn($"{this} - Failed to BeginRead", ex);
        Close();
      }
    }

    private void ReadCallback(IAsyncResult result)
    {
      if (state == HttpStates.Closed) {
        return;
      }

      State = HttpStates.Reading;

      try {
        var read = stream.EndRead(result);
        if (read <= 0) {
          // Zero is a graceful close, not a zero-length read.
          DebugFormat("{0} - Client closed the connection", this);
          Close();
          return;
        }
        DebugFormat("{0} - Read {1} bytes", this, read);
        readStream.Write(buffer, 0, read);
        lastActivity = DateTime.Now;
      }
      catch (Exception) {
        if (!IsATimeout) {
          WarnFormat("{0} - Failed to read data", this);
          Close();
        }
        return;
      }

      try {
        if (!hasHeaders) {
          // Re-parsed from the top on every read, so anything a partial pass
          // already picked up has to go first.
          Method = null;
          Path = null;
          Headers.Clear();
          readStream.Seek(0, SeekOrigin.Begin);
          var reader = new StreamReader(readStream);
          for (var line = reader.ReadLine();
            line != null;
            line = reader.ReadLine()) {
            line = line.Trim();
            if (string.IsNullOrEmpty(line)) {
              hasHeaders = true;
              // The reader has already buffered this out of readStream.
              var trailing = reader.ReadToEnd();
              readStream = StreamManager.GetStream();
              string cl;
              if (Headers.TryGetValue("content-length", out cl) &&
                  uint.TryParse(cl, out bodyBytes)) {
                if (bodyBytes > 1 << 20) {
                  throw new IOException("Body too long");
                }
                var ascii = Encoding.ASCII.GetBytes(trailing);
                readStream.Write(ascii, 0, ascii.Length);
                DebugFormat("Must read body bytes {0}", bodyBytes);
              }
              else if (trailing.Length != 0) {
                DebugFormat(
                  "{0} - {1} pipelined byte(s); not reusing the connection",
                  this, trailing.Length);
                pipelined = true;
              }
              break;
            }
            if (Method == null) {
              var parts = line.Split(new[] {' '}, 3);
              Method = parts[0].Trim().ToUpperInvariant();
              Path = parts[1].Trim();
              protocol = parts.Length > 2
                ? parts[2].Trim().ToUpperInvariant()
                : "HTTP/1.0";
              DebugFormat("{0} - {1} request for {2}", this, Method, Path);
            }
            else {
              var parts = line.Split(new[] {':'}, 2);
              Headers[parts[0]] = Uri.UnescapeDataString(parts[1]).Trim();
            }
          }
        }
        if (!hasHeaders) {
          DebugFormat("{0} - Headers incomplete, reading on", this);
          Read();
          return;
        }
        if (bodyBytes != 0 && bodyBytes > readStream.Length) {
          DebugFormat(
            "{0} - Bytes to go {1}", this, bodyBytes - readStream.Length);
          Read();
          return;
        }
        using (readStream) {
          Body = Encoding.UTF8.GetString(readStream.ToArray());
          Debug(Body);
          Debug(Headers);
        }
        SetupResponse();
      }
      catch (Exception ex) {
        Warn($"{this} - Failed to process request", ex);
        response = Error(HttpCode.InternalError);
        SendResponse();
      }
    }

    private void ReadNext()
    {
      Method = null;
      Path = null;
      protocol = "HTTP/1.1";
      Headers.Clear();
      hasHeaders = false;
      pipelined = false;
      Body = null;
      bodyBytes = 0;
      response = null;
      readStream = StreamManager.GetStream();

      ++requestCount;
      State = HttpStates.ReadBegin;

      Read();
    }

    /// <summary>
    ///   Whether the connection survives the response. HTTP/1.1 is persistent
    ///   unless the client says otherwise; HTTP/1.0 is the reverse.
    /// </summary>
    private bool WantsKeepAlive()
    {
      if (pipelined) {
        return false;
      }
      string conn;
      var stated = Headers.TryGetValue("connection", out conn) &&
                   !string.IsNullOrEmpty(conn);
      if (stated &&
          conn.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0) {
        return false;
      }
      if (protocol == "HTTP/1.0") {
        return stated &&
               conn.IndexOf("keep-alive", StringComparison.OrdinalIgnoreCase) >=
               0;
      }
      return true;
    }

    private void SendResponse()
    {
      var statusCode = response.Status;
      // Private copy: a handler may return a shared response object.
      var headers = new RawHeaders();
      foreach (var h in response.Headers) {
        headers[h.Key] = h.Value;
      }

      var original = response;
      var responseBody = ProcessRanges(headers, ref statusCode);
      if (!ReferenceEquals(original, response)) {
        // ProcessRanges swapped in an error response.
        headers.Clear();
        foreach (var h in response.Headers) {
          headers[h.Key] = h.Value;
        }
      }

      var keepAlive = WantsKeepAlive();
      headers["Connection"] = keepAlive ? "keep-alive" : "close";

      var responseStream = new ConcatenatedStream();
      try {
        var headerBlock = new StringBuilder();
        headerBlock.AppendFormat(
          "HTTP/1.1 {0} {1}\r\n",
          (uint)statusCode,
          HttpPhrases.Phrases[statusCode]
          );
        headerBlock.Append(headers.HeaderBlock);
        headerBlock.Append(CRLF);

        var headerStream = new MemoryStream(
          Encoding.ASCII.GetBytes(headerBlock.ToString()));
        responseStream.AddStream(headerStream);
        if (Method != "HEAD" && responseBody != null) {
          responseStream.AddStream(responseBody);
          responseBody = null;
        }
        InfoFormat("{0} - {1} response for {2}", this, (uint)statusCode, Path);
        state = HttpStates.Writing;
        // Held for exactly as long as the bytes are flowing, so the monitor
        // sees a stream that is aborted mid-way end just like one that
        // completes.
        var playback = StartPlayback();
        var sp = new StreamPump(responseStream, stream, BUFFER_SIZE);
        sp.Pump((pump, result) =>
        {
          playback?.Dispose();
          pump.Input.Close();
          pump.Input.Dispose();
          if (result == StreamPumpResult.Delivered) {
            DebugFormat("{0} - Done writing response", this);
            if (keepAlive) {
              ReadNext();
              return;
            }
          }
          else {
            DebugFormat("{0} - Client aborted connection", this);
          }
          Close();
        });
      }
      catch (Exception) {
        responseStream.Dispose();
        throw;
      }
      finally {
        responseBody?.Dispose();
      }
    }

    /// <summary>
    ///   Registers this transfer with the server's playback monitor, when it is
    ///   media content rather than a cover or subtitle. HEAD requests carry no
    ///   body and so are not playback.
    /// </summary>
    private IDisposable StartPlayback()
    {
      var media = response as IMediaStreamResponse;
      if (media == null || !media.IsPlayback || Method == "HEAD") {
        return null;
      }
      return owner.Playback.Begin(media.MediaItem, RemoteEndpoint.Address);
    }

    private void SetupResponse()
    {
      State = HttpStates.WriteBegin;
      try {
        if (!owner.AuthorizeClient(this)) {
          throw new HttpStatusException(HttpCode.Denied);
        }
        if (string.IsNullOrEmpty(Path)) {
          throw new HttpStatusException(HttpCode.NotFound);
        }
        var handler = owner.FindHandler(Path);
        if (handler == null) {
          throw new HttpStatusException(HttpCode.NotFound);
        }
        response = handler.HandleRequest(this);
        if (response == null) {
          throw new ArgumentException("Handler did not return a response");
        }
      }
      catch (HttpStatusException ex) {
#if DEBUG
        Warn($"{this} - Got a {ex.Code}: {Path}", ex);
#else
        InfoFormat("{0} - Got a {2}: {1}", this, Path, ex.Code);
#endif
        switch (ex.Code) {
        case HttpCode.NotFound:
          response = Error(HttpCode.NotFound);
          break;
        case HttpCode.Denied:
          response = Error(HttpCode.Denied);
          break;
        case HttpCode.InternalError:
          response = Error(HttpCode.InternalError);
          break;
        default:
          response = new StaticHandler(new StringResponse(
                                         ex.Code,
                                         "text/plain",
                                         ex.Message
                                         )).HandleRequest(this);
          break;
        }
      }
      catch (Exception ex) {
        Warn($"{this} - Failed to process response", ex);
        response = Error(HttpCode.InternalError);
      }
      SendResponse();
    }

    internal void Close()
    {
      State = HttpStates.Closed;

      DebugFormat(
        "{0} - Closing connection after {1} requests", this, requestCount);
      try {
        client.Close();
      }
      catch (Exception) {
        // ignored
      }
      owner.RemoveClient(this);
      if (stream != null) {
        try {
          stream.Dispose();
        }
        catch (ObjectDisposedException) {
        }
      }
    }

    public void Start()
    {
      ReadNext();
    }

    public override string ToString()
    {
      return RemoteEndpoint.ToString();
    }

    internal enum HttpStates
    {
      Accepted,
      Closed,
      ReadBegin,
      Reading,
      WriteBegin,
      Writing
    }
  }
}

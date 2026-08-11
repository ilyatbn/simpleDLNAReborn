using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;

namespace NMaier.SimpleDlna.Admin.Http
{
  /// <summary>
  ///   A small HTTP/1.1 server bound to the loopback interface, serving the
  ///   admin API and the web UI.
  /// </summary>
  /// <remarks>
  ///   The bind address is the entire security model: nothing off the machine
  ///   can reach this, including hosts the DLNA server happily streams to. See
  ///   modernization.md §2.11.
  /// </remarks>
  public sealed class AdminServer : IDisposable
  {
    private const int MAX_HEADER_BYTES = 64 * 1024;

    private const int MAX_BODY_BYTES = 8 * 1024 * 1024;

    private static readonly ILog log =
      LogManager.GetLogger(typeof (AdminServer));

    private readonly CancellationTokenSource cancel =
      new CancellationTokenSource();

    private readonly Func<AdminRequest, AdminResponse> handler;

    private readonly TcpListener listener;

    private bool disposed;

    public AdminServer(int port, Func<AdminRequest, AdminResponse> handler)
    {
      this.handler = handler ??
                     throw new ArgumentNullException(nameof(handler));
      listener = new TcpListener(IPAddress.Loopback, port);
      try {
        listener.Start();
      }
      catch (SocketException ex) {
        throw new AdminServerBindException(port, ex);
      }
      Port = ((IPEndPoint)listener.LocalEndpoint).Port;
      Task.Run(() => AcceptLoop(cancel.Token));
      log.InfoFormat("Admin API listening on http://127.0.0.1:{0}/", Port);
    }

    public int Port { get; }

    public string Url => $"http://localhost:{Port}/";

    public void Dispose()
    {
      if (disposed) {
        return;
      }
      disposed = true;
      try {
        cancel.Cancel();
      }
      catch (Exception) {
        // ignored
      }
      try {
        listener.Stop();
      }
      catch (Exception) {
        // ignored
      }
      cancel.Dispose();
    }

    private async Task AcceptLoop(CancellationToken token)
    {
      while (!token.IsCancellationRequested) {
        TcpClient client;
        try {
          client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException) {
          return;
        }
        catch (InvalidOperationException) {
          return;
        }
        catch (Exception ex) {
          if (token.IsCancellationRequested) {
            return;
          }
          log.Error("Failed to accept an admin client", ex);
          continue;
        }
        var accepted = client;
        var ignored = Task.Run(() => Serve(accepted, token));
      }
    }

    private void Serve(TcpClient client, CancellationToken token)
    {
      try {
        using (client) {
          var remote = client.Client.RemoteEndPoint as IPEndPoint;
          if (remote == null || !IPAddress.IsLoopback(remote.Address)) {
            // Cannot happen while bound to loopback, but the check is cheap and
            // survives someone widening the bind address later.
            log.WarnFormat("Rejecting non-loopback admin client {0}", remote);
            return;
          }
          client.NoDelay = true;
          client.ReceiveTimeout = 30000;
          client.SendTimeout = 300000;
          using (var stream = client.GetStream()) {
            while (!token.IsCancellationRequested) {
              if (!ServeOne(stream, remote, token)) {
                return;
              }
            }
          }
        }
      }
      catch (IOException) {
        // Client went away mid-request; normal.
      }
      catch (ObjectDisposedException) {
      }
      catch (Exception ex) {
        log.Error("Admin connection failed", ex);
      }
    }

    /// <summary>Returns true when the connection may be reused.</summary>
    private bool ServeOne(NetworkStream stream, IPEndPoint remote,
      CancellationToken token)
    {
      AdminRequest request;
      try {
        request = ReadRequest(stream, remote);
      }
      catch (BadRequestException ex) {
        Write(stream, AdminResponse.Text(ex.Status, ex.Message), false);
        return false;
      }
      if (request == null) {
        return false;
      }

      AdminResponse response;
      try {
        response = handler(request) ?? AdminResponse.Empty(404);
      }
      catch (Exception ex) {
        log.Error($"Admin handler failed for {request.RawTarget}", ex);
        response = AdminResponse.Text(500, "Internal Server Error");
      }

      if (response.StreamBody != null) {
        WriteStreamed(stream, response, token);
        return false;
      }

      var keepAlive = request.WantsKeepAlive();
      Write(stream, response, keepAlive);
      return keepAlive;
    }

    private static AdminRequest ReadRequest(NetworkStream stream,
      IPEndPoint remote)
    {
      var buffer = new MemoryStream();
      var chunk = new byte[4096];
      var headerEnd = -1;

      // Read until the blank line that ends the headers. Everything stays as
      // raw bytes so a UTF-8 body survives intact.
      while (headerEnd < 0) {
        int read;
        try {
          read = stream.Read(chunk, 0, chunk.Length);
        }
        catch (IOException) {
          return null;
        }
        if (read <= 0) {
          return null;
        }
        buffer.Write(chunk, 0, read);
        if (buffer.Length > MAX_HEADER_BYTES) {
          throw new BadRequestException(431, "Header section too large");
        }
        headerEnd = FindHeaderEnd(buffer.GetBuffer(), (int)buffer.Length);
      }

      var raw = buffer.GetBuffer();
      var total = (int)buffer.Length;
      var headerText = Encoding.UTF8.GetString(raw, 0, headerEnd);
      var lines = headerText.Split(new[] {"\r\n"}, StringSplitOptions.None);
      if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0])) {
        throw new BadRequestException(400, "Malformed request line");
      }

      var parts = lines[0].Split(new[] {' '}, 3);
      if (parts.Length < 2) {
        throw new BadRequestException(400, "Malformed request line");
      }
      var method = parts[0].Trim().ToUpperInvariant();
      var target = parts[1].Trim();

      var headers =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      for (var i = 1; i < lines.Length; ++i) {
        var line = lines[i];
        if (string.IsNullOrEmpty(line)) {
          continue;
        }
        var colon = line.IndexOf(':');
        if (colon <= 0) {
          continue;
        }
        // Header values are NOT percent-decoded. The DLNA server does that and
        // it corrupts any value containing a literal '%'.
        headers[line.Substring(0, colon).Trim()] =
          line.Substring(colon + 1).Trim();
      }

      if (headers.ContainsKey("Transfer-Encoding")) {
        throw new BadRequestException(
          400, "Chunked transfer encoding is not supported");
      }

      var bodyStart = headerEnd + 4;
      var already = Math.Max(0, total - bodyStart);
      var length = 0;
      string cl;
      if (headers.TryGetValue("Content-Length", out cl) &&
          !int.TryParse(cl, out length)) {
        throw new BadRequestException(400, "Malformed Content-Length");
      }
      if (length < 0) {
        throw new BadRequestException(400, "Malformed Content-Length");
      }
      if (length > MAX_BODY_BYTES) {
        throw new BadRequestException(413, "Body too large");
      }

      var body = new byte[length];
      if (length > 0) {
        var copy = Math.Min(already, length);
        Array.Copy(raw, bodyStart, body, 0, copy);
        var got = copy;
        while (got < length) {
          int read;
          try {
            read = stream.Read(body, got, length - got);
          }
          catch (IOException) {
            return null;
          }
          if (read <= 0) {
            throw new BadRequestException(400, "Truncated body");
          }
          got += read;
        }
      }

      return new AdminRequest(method, target, headers, body, remote);
    }

    private static int FindHeaderEnd(byte[] buffer, int length)
    {
      for (var i = 0; i + 3 < length; ++i) {
        if (buffer[i] == '\r' && buffer[i + 1] == '\n' &&
            buffer[i + 2] == '\r' && buffer[i + 3] == '\n') {
          return i;
        }
      }
      return -1;
    }

    private static void Write(NetworkStream stream, AdminResponse response,
      bool keepAlive)
    {
      var head = new StringBuilder();
      head.Append("HTTP/1.1 ").Append(response.Status).Append(' ')
        .Append(AdminResponse.Phrase(response.Status)).Append("\r\n");
      foreach (var h in response.Headers) {
        head.Append(h.Key).Append(": ").Append(h.Value).Append("\r\n");
      }
      head.Append("Content-Length: ").Append(response.Body.Length)
        .Append("\r\n");
      head.Append("Connection: ").Append(keepAlive ? "keep-alive" : "close")
        .Append("\r\n\r\n");

      var headBytes = Encoding.UTF8.GetBytes(head.ToString());
      stream.Write(headBytes, 0, headBytes.Length);
      if (response.Body.Length != 0) {
        stream.Write(response.Body, 0, response.Body.Length);
      }
      stream.Flush();
    }

    /// <summary>
    ///   Writes headers with no Content-Length and hands the socket to the
    ///   producer. This is what the DLNA stack cannot do - every response there
    ///   is a concatenated stream with a computed length.
    /// </summary>
    private static void WriteStreamed(NetworkStream stream,
      AdminResponse response, CancellationToken token)
    {
      var head = new StringBuilder();
      head.Append("HTTP/1.1 ").Append(response.Status).Append(' ')
        .Append(AdminResponse.Phrase(response.Status)).Append("\r\n");
      foreach (var h in response.Headers) {
        head.Append(h.Key).Append(": ").Append(h.Value).Append("\r\n");
      }
      head.Append("Connection: close\r\n\r\n");
      var headBytes = Encoding.UTF8.GetBytes(head.ToString());
      stream.Write(headBytes, 0, headBytes.Length);
      stream.Flush();
      try {
        response.StreamBody(stream);
      }
      catch (IOException) {
        // Client closed the event stream; expected.
      }
      catch (ObjectDisposedException) {
      }
      catch (Exception ex) {
        if (!token.IsCancellationRequested) {
          log.Debug("Streaming response ended", ex);
        }
      }
    }

    private sealed class BadRequestException : Exception
    {
      public BadRequestException(int status, string message)
        : base(message)
      {
        Status = status;
      }

      public int Status { get; }
    }
  }

  /// <summary>
  ///   Thrown when the admin port is already taken, so the host can say which
  ///   port and which flag changes it rather than dying with a bare
  ///   SocketException.
  /// </summary>
  public sealed class AdminServerBindException : Exception
  {
    public AdminServerBindException(int port, Exception inner)
      : base(
        $"Cannot listen on 127.0.0.1:{port} - the port is already in use. " +
        "Use --admin-port to choose another, or --no-admin to disable the " +
        "admin interface.", inner)
    {
      Port = port;
    }

    public int Port { get; }
  }
}

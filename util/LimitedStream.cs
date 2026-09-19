using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NMaier.SimpleDlna.Utilities
{
  /// <summary>
  ///   Exposes at most <paramref name="length" /> bytes of another stream,
  ///   starting wherever that stream currently is.
  /// </summary>
  /// <remarks>
  ///   This is what makes a bounded HTTP range request actually bounded. A
  ///   <c>Range: bytes=1000-50999</c> promises 50000 bytes in Content-Length,
  ///   and without a cap the response body runs to the end of the file
  ///   instead - which desynchronises the connection, rules out keep-alive,
  ///   and pushes megabytes nobody asked for.
  ///   <para>
  ///     Ownership of the inner stream transfers here: closing this closes it,
  ///     which is what keeps <c>FileReadStream</c> recycling into its cache.
  ///   </para>
  /// </remarks>
  public sealed class LimitedStream : Stream
  {
    private readonly Stream inner;

    private long remaining;

    public LimitedStream(Stream innerStream, long length)
    {
      if (length < 0) {
        throw new ArgumentOutOfRangeException(nameof(length));
      }
      inner = innerStream ??
              throw new ArgumentNullException(nameof(innerStream));
      remaining = length;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => remaining;

    public override long Position
    {
      get { throw new NotSupportedException(); }
      set { throw new NotSupportedException(); }
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
      var take = Clamp(count);
      if (take == 0) {
        return 0;
      }
      var read = inner.Read(buffer, offset, take);
      remaining -= read;
      return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset,
      int count, CancellationToken cancellationToken)
    {
      var take = Clamp(count);
      if (take == 0) {
        return 0;
      }
      var read = await inner
        .ReadAsync(buffer, offset, take, cancellationToken)
        .ConfigureAwait(false);
      remaining -= read;
      return read;
    }

    private int Clamp(int count)
    {
      if (count <= 0 || remaining <= 0) {
        return 0;
      }
      return remaining < count ? (int)remaining : count;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
      throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
      throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
      throw new NotSupportedException();
    }

    public override void Close()
    {
      inner.Close();
      base.Close();
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing) {
        inner.Dispose();
      }
      base.Dispose(disposing);
    }
  }
}

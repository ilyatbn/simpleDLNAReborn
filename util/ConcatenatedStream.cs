using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NMaier.SimpleDlna.Utilities
{
  /// <summary>
  ///   Reads a queue of streams end to end as though they were one.
  /// </summary>
  /// <remarks>
  ///   Returns a short read at every boundary rather than topping the buffer
  ///   up from the next stream. ReadAsync is overridden on purpose - see
  ///   util/CLAUDE.md.
  /// </remarks>
  public sealed class ConcatenatedStream : Stream
  {
    private readonly Queue<Stream> streams = new Queue<Stream>();

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length
    {
      get { throw new NotSupportedException(); }
    }

    public override long Position
    {
      get { throw new NotSupportedException(); }
      set { throw new NotSupportedException(); }
    }

    public void AddStream(Stream stream)
    {
      streams.Enqueue(stream);
    }

    public override void Close()
    {
      foreach (var stream in streams) {
        stream.Close();
        stream.Dispose();
      }
      streams.Clear();
      base.Close();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
      while (streams.Count != 0) {
        var read = streams.Peek().Read(buffer, offset, count);
        if (read > 0) {
          return read;
        }
        Advance();
      }
      return 0;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset,
      int count, CancellationToken cancellationToken)
    {
      while (streams.Count != 0) {
        var read = await streams.Peek()
          .ReadAsync(buffer, offset, count, cancellationToken)
          .ConfigureAwait(false);
        if (read > 0) {
          return read;
        }
        Advance();
      }
      return 0;
    }

    private void Advance()
    {
      var done = streams.Dequeue();
      done.Close();
      done.Dispose();
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
  }
}

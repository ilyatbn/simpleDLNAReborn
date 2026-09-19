using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using log4net;

namespace NMaier.SimpleDlna.Utilities
{
  /// <summary>
  ///   Copies one stream into another, overlapping the read of the next block
  ///   with the write of the current one.
  /// </summary>
  /// <remarks>Double buffered and ReadAsync-based - see util/CLAUDE.md.</remarks>
  public sealed class StreamPump : IDisposable
  {
    private static readonly ILog logger =
      LogManager.GetLogger(typeof (StreamPump));

    private readonly byte[] bufferA;

    private readonly byte[] bufferB;

    private readonly SemaphoreSlim sem = new SemaphoreSlim(0, 1);

    public StreamPump(Stream inputStream, Stream outputStream, int bufferSize)
    {
      bufferA = new byte[bufferSize];
      bufferB = new byte[bufferSize];
      Input = inputStream;
      Output = outputStream;
    }

    public Stream Input { get; }

    public Stream Output { get; }

    public void Dispose()
    {
      sem.Dispose();
    }

    public void Pump(StreamPumpCallback callback)
    {
      var ignored = RunAsync(callback);
    }

    private async Task RunAsync(StreamPumpCallback callback)
    {
      var result = StreamPumpResult.Delivered;
      try {
        var current = bufferA;
        var spare = bufferB;
        var read = await Input.ReadAsync(current, 0, current.Length)
          .ConfigureAwait(false);

        while (read > 0) {
          var writing = Output.WriteAsync(current, 0, read);
          var reading = Input.ReadAsync(spare, 0, spare.Length);

          // Both awaited even on fault, so neither goes unobserved.
          Exception failure = null;
          var next = 0;
          try {
            next = await reading.ConfigureAwait(false);
          }
          catch (Exception ex) {
            failure = ex;
          }
          try {
            await writing.ConfigureAwait(false);
          }
          catch (Exception ex) {
            failure = failure ?? ex;
          }
          if (failure != null) {
            throw failure;
          }

          var swap = current;
          current = spare;
          spare = swap;
          read = next;
        }
      }
      catch (Exception ex) {
        logger.Debug("Stream pump aborted", ex);
        result = StreamPumpResult.Aborted;
      }
      Finish(result, callback);
    }

    private void Finish(StreamPumpResult result, StreamPumpCallback callback)
    {
      if (callback != null) {
        ThreadPool.QueueUserWorkItem(_ =>
        {
          try {
            callback(this, result);
          }
          catch (Exception ex) {
            logger.Error("Stream pump callback failed", ex);
          }
        });
      }
      try {
        sem.Release();
      }
      catch (ObjectDisposedException) {
        // Nobody is waiting any more.
      }
      catch (Exception ex) {
        logger.Error(ex.Message, ex);
      }
    }

    public bool Wait(int timeout)
    {
      return sem.Wait(timeout);
    }
  }
}

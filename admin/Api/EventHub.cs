using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace NMaier.SimpleDlna.Admin.Api
{
  /// <summary>
  ///   Fan-out for Server-Sent Events.
  /// </summary>
  /// <remarks>
  ///   Events are nudges, not state transfer: the client refetches whatever
  ///   changed. That means no ordering or replay guarantees are needed, and a
  ///   subscriber that falls behind can simply drop events.
  /// </remarks>
  public sealed class EventHub : IDisposable
  {
    private readonly List<Subscriber> subscribers = new List<Subscriber>();

    private readonly object sync = new object();

    public void Dispose()
    {
      lock (sync) {
        foreach (var s in subscribers.ToArray()) {
          s.Complete();
        }
        subscribers.Clear();
      }
    }

    public Subscriber Subscribe()
    {
      var rv = new Subscriber(this);
      lock (sync) {
        subscribers.Add(rv);
      }
      return rv;
    }

    public void Publish(string name, string data)
    {
      var payload = $"event: {name}\ndata: {data}\n\n";
      lock (sync) {
        foreach (var s in subscribers.ToArray()) {
          s.Offer(payload);
        }
      }
    }

    private void Remove(Subscriber subscriber)
    {
      lock (sync) {
        subscribers.Remove(subscriber);
      }
    }

    public sealed class Subscriber : IDisposable
    {
      private const int CAPACITY = 64;

      private readonly BlockingCollection<string> queue =
        new BlockingCollection<string>(CAPACITY);

      private readonly EventHub owner;

      internal Subscriber(EventHub owner)
      {
        this.owner = owner;
      }

      public void Dispose()
      {
        owner.Remove(this);
        try {
          queue.Dispose();
        }
        catch (Exception) {
          // ignored
        }
      }

      internal void Offer(string payload)
      {
        try {
          // Never block the publisher: a stalled browser tab must not wedge
          // the server's state machine.
          queue.TryAdd(payload);
        }
        catch (Exception) {
          // ignored
        }
      }

      internal void Complete()
      {
        try {
          queue.CompleteAdding();
        }
        catch (Exception) {
          // ignored
        }
      }

      /// <summary>
      ///   Waits for the next payload, or returns null when the wait times out
      ///   so the caller can emit a keepalive.
      /// </summary>
      public string Take(TimeSpan timeout, CancellationToken token)
      {
        try {
          string rv;
          return queue.TryTake(out rv, (int)timeout.TotalMilliseconds, token)
            ? rv
            : null;
        }
        catch (OperationCanceledException) {
          return null;
        }
        catch (ObjectDisposedException) {
          return null;
        }
        catch (InvalidOperationException) {
          return null;
        }
      }
    }
  }
}

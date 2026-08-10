using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Timers;
using NMaier.SimpleDlna.Utilities;
using Timer = System.Timers.Timer;

namespace NMaier.SimpleDlna.Server
{
  /// <summary>
  ///   One media item currently being streamed to one client.
  /// </summary>
  public sealed class PlaybackSession
  {
    internal PlaybackSession(IMediaResource item, IPAddress client)
    {
      Title = item.Title;
      MediaType = item.MediaType;
      Client = client;
      Started = DateTime.UtcNow;
    }

    public string Title { get; }

    public DlnaMediaTypes MediaType { get; }

    public IPAddress Client { get; }

    public DateTime Started { get; }

    public override string ToString()
    {
      return $"{Title} ({Client})";
    }
  }

  /// <summary>
  ///   Tracks whether anything is actually being played right now.
  /// </summary>
  /// <remarks>
  ///   Deliberately generic: it only knows "a media stream is open to a
  ///   client". Consumers decide what to do with that - keeping the machine
  ///   awake, showing an indicator, and whatever comes later - by reading
  ///   <see cref="IsPlaying" /> or subscribing to <see cref="Changed" />.
  ///
  ///   A player does not hold one long connection open. Clients routinely close
  ///   a range request and immediately open the next, so raw connection counting
  ///   flickers between playing and idle several times a minute. Playback
  ///   therefore stays "on" for <see cref="Grace" /> after the last stream ends,
  ///   and only then reports idle.
  /// </remarks>
  public sealed class PlaybackMonitor : Logging, IDisposable
  {
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(15);

    private readonly object sync = new object();

    private readonly Dictionary<Guid, PlaybackSession> active =
      new Dictionary<Guid, PlaybackSession>();

    private readonly Timer expiry = new Timer(
      TimeSpan.FromSeconds(1).TotalMilliseconds);

    private PlaybackSession last;

    private DateTime lastEnded = DateTime.MinValue;

    private bool wasPlaying;

    public PlaybackMonitor()
    {
      Grace = DefaultGrace;
      expiry.Elapsed += CheckExpiry;
      expiry.Enabled = true;
    }

    /// <summary>
    ///   How long playback keeps reporting active after the last stream closed.
    /// </summary>
    public TimeSpan Grace { get; set; }

    /// <summary>Raised whenever <see cref="IsPlaying" /> flips.</summary>
    public event EventHandler Changed;

    public bool IsPlaying
    {
      get {
        lock (sync) {
          return IsPlayingLocked();
        }
      }
    }

    /// <summary>
    ///   What is playing, or what played most recently within the grace window.
    ///   Null when idle.
    /// </summary>
    public PlaybackSession Current
    {
      get {
        lock (sync) {
          if (active.Count > 0) {
            return active.Values.First();
          }
          return IsPlayingLocked() ? last : null;
        }
      }
    }

    public IList<PlaybackSession> Active
    {
      get {
        lock (sync) {
          return active.Values.ToList();
        }
      }
    }

    public void Dispose()
    {
      expiry.Elapsed -= CheckExpiry;
      expiry.Dispose();
    }

    /// <summary>
    ///   Marks the start of a stream. Dispose the result when the transfer
    ///   finishes or is aborted.
    /// </summary>
    public IDisposable Begin(IMediaResource item, IPAddress client)
    {
      if (item == null) {
        return new Session(this, Guid.Empty);
      }
      var id = Guid.NewGuid();
      lock (sync) {
        var session = new PlaybackSession(item, client);
        active[id] = session;
        last = session;
      }
      // Per-stream, so it fires for every range request a player makes - too
      // noisy for anything above Debug. The state transition is logged once, in
      // RaiseIfChanged.
      DebugFormat("Stream started: {0} -> {1}", item.Title, client);
      RaiseIfChanged();
      return new Session(this, id);
    }

    private void End(Guid id)
    {
      if (id == Guid.Empty) {
        return;
      }
      lock (sync) {
        if (!active.Remove(id)) {
          return;
        }
        if (active.Count == 0) {
          lastEnded = DateTime.UtcNow;
        }
      }
      RaiseIfChanged();
    }

    private bool IsPlayingLocked()
    {
      if (active.Count > 0) {
        return true;
      }
      return last != null && DateTime.UtcNow - lastEnded < Grace;
    }

    private void CheckExpiry(object sender, ElapsedEventArgs e)
    {
      RaiseIfChanged();
    }

    private void RaiseIfChanged()
    {
      bool playing;
      PlaybackSession session;
      lock (sync) {
        playing = IsPlayingLocked();
        if (playing == wasPlaying) {
          return;
        }
        wasPlaying = playing;
        session = last;
      }
      if (playing) {
        InfoFormat("Playback started: {0}", session);
      }
      else {
        Info("Playback stopped");
      }
      try {
        Changed?.Invoke(this, EventArgs.Empty);
      }
      catch (Exception ex) {
        Error("A playback listener failed", ex);
      }
    }

    private sealed class Session : IDisposable
    {
      private readonly Guid id;

      private readonly PlaybackMonitor owner;

      private bool disposed;

      public Session(PlaybackMonitor owner, Guid id)
      {
        this.owner = owner;
        this.id = id;
      }

      public void Dispose()
      {
        if (disposed) {
          return;
        }
        disposed = true;
        owner.End(id);
      }
    }
  }
}

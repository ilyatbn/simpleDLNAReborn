using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using log4net;
using NMaier.SimpleDlna.FileMediaServer;
using NMaier.SimpleDlna.Server;

namespace NMaier.SimpleDlna.Admin
{
  /// <summary>
  ///   Owns every configured server, its lifecycle and its persistence.
  /// </summary>
  /// <remarks>
  ///   UI-free by design: this is the single implementation both the tray host
  ///   and the REST API drive, so the two cannot drift. It is also the only
  ///   writer of descriptors.xml.
  /// </remarks>
  public sealed class ServerManager : IDisposable
  {
    private static readonly ILog log =
      LogManager.GetLogger(typeof (ServerManager));

    private readonly List<ManagedServer> servers = new List<ManagedServer>();

    private readonly object sync = new object();

    private bool disposed;

    public ServerManager(HttpServer server, DescriptorStore store,
      ServerManagerOptions options)
    {
      Server = server ?? throw new ArgumentNullException(nameof(server));
      Store = store ?? throw new ArgumentNullException(nameof(store));
      Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public HttpServer Server { get; }

    public DescriptorStore Store { get; }

    public ServerManagerOptions Options { get; }

    /// <summary>
    ///   When false, <see cref="Save" /> does nothing. The console adopts
    ///   command-line servers and must never write them over the descriptors
    ///   file the tray host owns.
    /// </summary>
    public bool Persist { get; set; } = true;

    /// <summary>
    ///   Registers an already-built, already-mounted server so it shows up in
    ///   the API. Used by the console in command-line mode.
    /// </summary>
    public ManagedServer Adopt(FileServer fileServer,
      ServerDescription description)
    {
      if (fileServer == null) {
        throw new ArgumentNullException(nameof(fileServer));
      }
      Normalize(description);
      description.Active = true;
      description.EnsureId();
      ManagedServer rv;
      lock (sync) {
        rv = new ManagedServer(this, description, fileServer);
        servers.Add(rv);
      }
      OnListChanged();
      return rv;
    }

    /// <summary>Raised on every state transition, from arbitrary threads.</summary>
    public event EventHandler<ServerStateChangedEventArgs> StateChanged;

    /// <summary>Raised when a server is added or removed.</summary>
    public event EventHandler ListChanged;

    public IReadOnlyList<ManagedServer> Servers
    {
      get {
        lock (sync) {
          return servers.ToArray();
        }
      }
    }

    public void Dispose()
    {
      lock (sync) {
        if (disposed) {
          return;
        }
        disposed = true;
        foreach (var s in servers) {
          try {
            s.Dispose();
          }
          catch (Exception ex) {
            log.Error("Failed to dispose a server", ex);
          }
        }
        servers.Clear();
      }
    }

    /// <summary>
    ///   Loads descriptors.xml and starts every active server, in parallel.
    /// </summary>
    /// <param name="legacy">
    ///   Fallback used only when descriptors.xml is missing or unreadable, so
    ///   configurations stored in the old user.config still migrate.
    /// </param>
    public void Load(IEnumerable<ServerDescription> legacy = null)
    {
      var descriptions = Store.Load();
      if (descriptions == null && legacy != null) {
        try {
          descriptions = legacy.ToList();
          if (descriptions.Count != 0) {
            log.InfoFormat(
              "Migrating {0} server(s) from the legacy configuration",
              descriptions.Count);
          }
        }
        catch (Exception ex) {
          log.Error("Failed to read the legacy configuration", ex);
          descriptions = null;
        }
      }
      descriptions = descriptions ?? new List<ServerDescription>();

      var assigned = false;
      lock (sync) {
        foreach (var d in descriptions) {
          if (d == null) {
            continue;
          }
          Normalize(d);
          assigned |= d.EnsureId();
          servers.Add(new ManagedServer(this, d));
        }
      }
      if (assigned) {
        Save();
      }
      OnListChanged();

      var snapshot = Servers;
      var po = new ParallelOptions
      {
        MaxDegreeOfParallelism = Math.Min(2, Environment.ProcessorCount)
      };
      Parallel.ForEach(snapshot, po, s => s.Load());
    }

    public ManagedServer Find(Guid id)
    {
      lock (sync) {
        return servers.FirstOrDefault(s => s.Id == id);
      }
    }

    public ManagedServer Add(ServerDescription description)
    {
      if (description == null) {
        throw new ArgumentNullException(nameof(description));
      }
      Normalize(description);
      description.EnsureId();
      ManagedServer server;
      lock (sync) {
        server = new ManagedServer(this, description);
        servers.Add(server);
      }
      OnListChanged();
      server.Load();
      Save();
      return server;
    }

    /// <summary>
    ///   Applies a new configuration. A running server is stopped and started
    ///   again, which is what the GUI did and what mount re-registration needs.
    /// </summary>
    public ManagedServer Update(Guid id, ServerDescription description)
    {
      if (description == null) {
        throw new ArgumentNullException(nameof(description));
      }
      var server = Find(id);
      if (server == null) {
        return null;
      }
      Normalize(description);
      server.UpdateInfo(description);
      Save();
      return server;
    }

    public bool Remove(Guid id)
    {
      var server = Find(id);
      if (server == null) {
        return false;
      }
      server.StopFileServer();
      lock (sync) {
        servers.Remove(server);
      }
      server.Dispose();
      OnListChanged();
      Save();
      return true;
    }

    public void Start(Guid id)
    {
      var server = Find(id);
      if (server == null) {
        return;
      }
      if (!server.Description.Active) {
        server.Description.Active = true;
      }
      server.StartFileServer();
      Save();
    }

    public void Stop(Guid id)
    {
      var server = Find(id);
      if (server == null) {
        return;
      }
      server.StopFileServer();
      server.Description.Active = false;
      Save();
    }

    /// <summary>Start if stopped, stop if running.</summary>
    public void Toggle(Guid id)
    {
      var server = Find(id);
      if (server == null) {
        return;
      }
      server.Toggle();
      Save();
    }

    /// <summary>
    ///   Stops and starts one server, keeping it active throughout.
    /// </summary>
    public void Restart(Guid id)
    {
      var server = Find(id);
      if (server == null) {
        throw new InvalidOperationException("No such server");
      }
      server.Restart();
      // Only the state changed, but a failed start flips Active off, and that
      // does need persisting.
      Save();
    }

    public void Rescan(Guid id)
    {
      var server = Find(id);
      if (server == null) {
        throw new InvalidOperationException("No such server");
      }
      server.Rescan();
    }

    /// <summary>
    ///   Rescans everything that is running. Returns how many were asked and
    ///   how many were skipped, which the GUI silently swallowed.
    /// </summary>
    public RescanAllResult RescanAll()
    {
      var requested = 0;
      var skipped = 0;
      foreach (var s in Servers) {
        try {
          s.Rescan();
          ++requested;
        }
        catch (Exception) {
          ++skipped;
        }
      }
      return new RescanAllResult(requested, skipped);
    }

    /// <summary>
    ///   Stops and starts everything that is running, in sequence.
    /// </summary>
    /// <remarks>
    ///   Sequential on purpose. Each restart reloads a library and re-registers
    ///   a mount, and doing several at once buys nothing while making the log
    ///   and the SSDP traffic much harder to read. Servers that are stopped are
    ///   left alone - starting them would be a policy decision this is not.
    /// </remarks>
    public RestartAllResult RestartAll()
    {
      var restarted = 0;
      var failed = 0;
      foreach (var s in Servers) {
        if (!s.IsRunning) {
          continue;
        }
        try {
          s.Restart();
          // A failed start flips Active off and leaves the server stopped;
          // ManagedServer already logged why.
          if (s.IsRunning) {
            ++restarted;
          }
          else {
            ++failed;
          }
        }
        catch (Exception ex) {
          log.Error($"Failed to restart {s.Description.Name}", ex);
          ++failed;
        }
      }
      Save();
      return new RestartAllResult(restarted, failed);
    }

    /// <summary>
    ///   Re-announces every mount on the addresses the machine holds now,
    ///   without reloading anything.
    /// </summary>
    /// <returns>How many mounts were re-announced.</returns>
    public int Readvertise()
    {
      return Server.Readvertise();
    }

    /// <summary>
    ///   Stops every running server, deletes the metadata cache and starts them
    ///   again.
    /// </summary>
    public void DropCache()
    {
      var running = Servers.Where(s => s.Description.Active).ToList();
      foreach (var s in running) {
        s.StopFileServer();
      }
      try {
        var cache = Options.CacheFile;
        if (cache != null) {
          cache.Refresh();
          if (cache.Exists) {
            cache.Delete();
          }
        }
      }
      catch (Exception ex) {
        log.Error("Failed to remove the cache file", ex);
      }
      foreach (var s in running) {
        s.StartFileServer();
      }
    }

    public void Save()
    {
      if (!Persist) {
        return;
      }
      Store.Save(Servers.Select(s => s.Description));
    }

    internal void RaiseStateChanged(ManagedServer server)
    {
      try {
        StateChanged?.Invoke(this, new ServerStateChangedEventArgs(server));
      }
      catch (Exception ex) {
        log.Error("A state listener failed", ex);
      }
    }

    private void OnListChanged()
    {
      try {
        ListChanged?.Invoke(this, EventArgs.Empty);
      }
      catch (Exception ex) {
        log.Error("A list listener failed", ex);
      }
    }

    /// <summary>
    ///   Defends the rest of the code from nulls in a hand-edited or
    ///   partially-written descriptors.xml.
    /// </summary>
    private static void Normalize(ServerDescription d)
    {
      d.Directories = d.Directories ?? new string[0];
      d.Views = d.Views ?? new string[0];
      d.Ips = d.Ips ?? new string[0];
      d.Macs = d.Macs ?? new string[0];
      d.UserAgents = d.UserAgents ?? new string[0];
      d.Name = d.Name ?? string.Empty;
      if (string.IsNullOrWhiteSpace(d.Order)) {
        d.Order = "title";
      }
    }
  }

  public struct RescanAllResult
  {
    internal RescanAllResult(int requested, int skipped)
    {
      Requested = requested;
      Skipped = skipped;
    }

    public int Requested { get; }

    public int Skipped { get; }
  }

  public struct RestartAllResult
  {
    internal RestartAllResult(int restarted, int failed)
    {
      Restarted = restarted;
      Failed = failed;
    }

    public int Restarted { get; }

    public int Failed { get; }
  }
}

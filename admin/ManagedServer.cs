using System;
using System.IO;
using System.Linq;
using log4net;
using log4net.Core;
using NMaier.SimpleDlna.FileMediaServer;
using NMaier.SimpleDlna.Server;
using NMaier.SimpleDlna.Server.Comparers;

namespace NMaier.SimpleDlna.Admin
{
  /// <summary>
  ///   One configured server plus its live <see cref="FileServer" />.
  /// </summary>
  /// <remarks>
  ///   This is the WinForms <c>ServerListViewItem</c> with the ListViewItem
  ///   taken away. The start/stop/rescan semantics are deliberately unchanged -
  ///   see modernization.md §1.9 for the behaviour this must preserve.
  /// </remarks>
  public sealed class ManagedServer : IDisposable
  {
    private static readonly ILog log =
      LogManager.GetLogger(typeof (ManagedServer));

    private readonly ServerManager owner;

    private readonly object sync = new object();

    private FileServer fileServer;

    private ServerState state = ServerState.Idle;

    internal ManagedServer(ServerManager owner, ServerDescription description)
    {
      this.owner = owner;
      Description = description;
    }

    public Guid Id => Description.Id;

    public ServerDescription Description { get; }

    public ServerState State
    {
      get { return state; }
      private set {
        if (state == value) {
          return;
        }
        state = value;
        owner.RaiseStateChanged(this);
      }
    }

    /// <summary>
    ///   Why the last start failed, or null. The GUI logged this and threw it
    ///   away; the API surfaces it.
    /// </summary>
    public string LastError { get; private set; }

    public DateTime? StartedUtc { get; private set; }

    public double? LoadSeconds { get; private set; }

    /// <summary>UUID of the running mount, or null while stopped.</summary>
    public Guid? Uuid => fileServer?.UUID;

    /// <summary>
    ///   HTTP prefix the mount is registered under, e.g. "/mm-3/", or null.
    /// </summary>
    public string MountPrefix { get; private set; }

    public bool IsRunning => fileServer != null;

    public void Dispose()
    {
      lock (sync) {
        if (fileServer == null) {
          return;
        }
        try {
          owner.Server.UnregisterMediaServer(fileServer);
        }
        catch (Exception ex) {
          log.Error("Failed to unregister on dispose", ex);
        }
        fileServer.Dispose();
        fileServer = null;
      }
    }

    /// <summary>
    ///   Builds and registers the <see cref="FileServer" />, unless the
    ///   description is inactive.
    /// </summary>
    internal void StartFileServer()
    {
      lock (sync) {
        if (!Description.Active) {
          State = ServerState.Stopped;
          return;
        }
        if (fileServer != null) {
          return;
        }
        var start = DateTime.Now;
        try {
          State = ServerState.Loading;
          LastError = null;

          var ids = new Identifiers(
            ComparerRepository.Lookup(Description.Order),
            Description.OrderDescending);
          foreach (var v in Description.Views) {
            ids.AddView(v);
          }
          var dirs = (from i in Description.Directories
                      let d = new DirectoryInfo(i)
                      where d.Exists
                      select d).ToArray();
          if (dirs.Length == 0) {
            throw new InvalidOperationException("No remaining directories");
          }

          var server = new FileServer(Description.Types, ids, dirs)
          {
            FriendlyName = Description.Name,
            ChangeDelay = owner.Options.ChangeDelay,
            // Zero means "watcher only"; FileServer reads it as disabled.
            RescanInterval = owner.Options.RescanInterval
          };
          if (owner.Options.CacheFile != null) {
            server.SetCacheFile(owner.Options.CacheFile);
          }
          server.Changing += OnChanging;
          server.Changed += OnChanged;
          server.Load();

          var authorizer = new HttpAuthorizer();
          if (Description.Ips.Length != 0) {
            authorizer.AddMethod(new IPAddressAuthorizer(Description.Ips));
          }
          if (Description.Macs.Length != 0) {
            authorizer.AddMethod(new MacAuthorizer(Description.Macs));
          }
          if (Description.UserAgents.Length != 0) {
            authorizer.AddMethod(
              new UserAgentAuthorizer(Description.UserAgents));
          }
          server.Authorizer = authorizer;

          owner.Server.RegisterMediaServer(server);
          fileServer = server;

          var elapsed = DateTime.Now - start;
          LoadSeconds = elapsed.TotalSeconds;
          StartedUtc = DateTime.UtcNow;
          MountPrefix = FindMountPrefix(server);
          State = ServerState.Running;

          LogManager.GetLogger("State").Logger.Log(
            GetType(),
            Level.Notice,
            $"{server.FriendlyName} loaded in {elapsed.TotalSeconds:F2} seconds",
            null
            );
        }
        catch (Exception ex) {
          log.Error($"Failed to start {Description.Name}", ex);
          LastError = ex.Message;
          // Matches the GUI: a failed start flips the server back to inactive
          // instead of retrying forever.
          Description.Active = false;
          StartedUtc = null;
          LoadSeconds = null;
          MountPrefix = null;
          if (fileServer != null) {
            fileServer.Dispose();
            fileServer = null;
          }
          State = ServerState.Stopped;
        }
      }
    }

    internal void StopFileServer()
    {
      lock (sync) {
        if (fileServer == null) {
          State = ServerState.Stopped;
          return;
        }
        owner.Server.UnregisterMediaServer(fileServer);
        fileServer.Changing -= OnChanging;
        fileServer.Changed -= OnChanged;
        fileServer.Dispose();
        fileServer = null;
        StartedUtc = null;
        MountPrefix = null;
        State = ServerState.Stopped;
      }
    }

    internal void Load()
    {
      State = ServerState.Loading;
      StartFileServer();
    }

    internal void Toggle()
    {
      StopFileServer();
      Description.ToggleActive();
      StartFileServer();
    }

    internal void UpdateInfo(ServerDescription description)
    {
      StopFileServer();
      Description.AdoptInfo(description);
      StartFileServer();
    }

    public void Rescan()
    {
      var vs = fileServer as IVolatileMediaServer;
      if (fileServer == null) {
        throw new InvalidOperationException("Server is not running");
      }
      if (vs == null) {
        throw new InvalidOperationException(
          "Server does not support rescanning");
      }
      vs.Rescan();
    }

    private void OnChanging(object sender, EventArgs e)
    {
      State = ServerState.Refreshing;
    }

    private void OnChanged(object sender, EventArgs e)
    {
      State = Description.Active ? ServerState.Running : ServerState.Stopped;
    }

    /// <summary>
    ///   Best effort: MediaMounts is keyed by prefix and valued by friendly
    ///   name, so identically named servers can shadow each other. Cosmetic
    ///   only.
    /// </summary>
    private string FindMountPrefix(FileServer server)
    {
      try {
        return (from m in owner.Server.MediaMounts
                where m.Value == server.FriendlyName
                select m.Key).FirstOrDefault();
      }
      catch (Exception) {
        return null;
      }
    }
  }
}

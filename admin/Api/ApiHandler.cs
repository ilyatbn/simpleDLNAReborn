using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using NMaier.SimpleDlna.Admin.Http;
using NMaier.SimpleDlna.Server;
using NMaier.SimpleDlna.Server.Comparers;
using NMaier.SimpleDlna.Server.Views;
using NMaier.SimpleDlna.Utilities;

namespace NMaier.SimpleDlna.Admin.Api
{
  /// <summary>
  ///   Implements /api/v1.
  /// </summary>
  public sealed class ApiHandler
  {
    public const string PREFIX = "/api/v1";

    private static readonly ILog log =
      LogManager.GetLogger(typeof (ApiHandler));

    internal static readonly JsonSerializerOptions JsonOptions =
      new JsonSerializerOptions
      {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition =
          System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
      };

    private readonly AdminContext context;

    public ApiHandler(AdminContext context)
    {
      this.context = context;
    }

    public AdminResponse Handle(AdminRequest request)
    {
      var path = request.Path.Substring(PREFIX.Length);
      if (path.Length == 0) {
        path = "/";
      }
      var segments = path.Split(new[] {'/'},
        StringSplitOptions.RemoveEmptyEntries);

      var guard = Guard(request);
      if (guard != null) {
        return guard;
      }

      try {
        return Dispatch(request, segments);
      }
      catch (ApiException ex) {
        return Error(ex.Status, ex.Code, ex.Message, ex.Details);
      }
      catch (Exception ex) {
        log.Error($"Unhandled API failure for {request.RawTarget}", ex);
        return Error(500, "internal_error", ex.Message);
      }
    }

    /// <summary>
    ///   Cross-origin and content-type defences. Loopback binding is the real
    ///   security boundary; these stop a malicious local page from driving the
    ///   API with simple requests.
    /// </summary>
    private AdminResponse Guard(AdminRequest request)
    {
      var origin = request.Header("origin");
      if (!string.IsNullOrEmpty(origin) && !IsLocalOrigin(origin)) {
        return Error(403, "forbidden_origin",
          "Requests from other origins are not accepted.");
      }
      if (IsMutation(request.Method)) {
        var type = request.Header("content-type") ?? string.Empty;
        var hasBody = request.BodyBytes.Length != 0;
        if (hasBody &&
            type.IndexOf("application/json",
              StringComparison.OrdinalIgnoreCase) < 0) {
          return Error(415, "unsupported_media_type",
            "Request bodies must be application/json.");
        }
      }
      return null;
    }

    private bool IsLocalOrigin(string origin)
    {
      Uri uri;
      if (!Uri.TryCreate(origin, UriKind.Absolute, out uri)) {
        return false;
      }
      if (!uri.IsLoopback) {
        return false;
      }
      // The Vite dev server proxies from another port, so any loopback origin
      // is accepted; the bind address already keeps this machine-local.
      return true;
    }

    private static bool IsMutation(string method)
    {
      return method == "POST" || method == "PUT" || method == "DELETE" ||
             method == "PATCH";
    }

    private AdminResponse Dispatch(AdminRequest request, string[] s)
    {
      var m = request.Method;
      if (m == "OPTIONS") {
        return AdminResponse.Empty(204);
      }

      if (s.Length == 1) {
        switch (s[0]) {
        case "status":
          return Get(m, () => Ok(BuildStatus()));
        case "capabilities":
          return Get(m, () => Ok(BuildCapabilities()));
        case "servers":
          if (m == "GET") {
            return Ok(new ServerListDto
            {
              Servers = context.Manager.Servers.Select(ToDto).ToList()
            });
          }
          if (m == "POST") {
            return CreateServer(request);
          }
          return MethodNotAllowed();
        case "settings":
          if (m == "GET") {
            return Ok(BuildSettings(new string[0]));
          }
          if (m == "PUT") {
            return UpdateSettings(request);
          }
          return MethodNotAllowed();
        case "log":
          return Get(m, () => Ok(ReadLog(request)));
        case "fs":
          return Get(m, () => BrowseFs(request));
        case "events":
          return Get(m, () => Events());
        }
      }

      if (s.Length == 2 && s[0] == "cache" && s[1] == "drop") {
        if (m != "POST") {
          return MethodNotAllowed();
        }
        RequireManaged();
        Task.Run(() =>
        {
          try {
            context.Manager.DropCache();
          }
          catch (Exception ex) {
            log.Error("Dropping the cache failed", ex);
          }
        });
        return AdminResponse.Empty(202);
      }

      if (s.Length >= 2 && s[0] == "servers") {
        if (s[1] == "rescan-all" && s.Length == 2) {
          if (m != "POST") {
            return MethodNotAllowed();
          }
          var result = context.Manager.RescanAll();
          return Ok(new RescanAllDto
          {
            Requested = result.Requested,
            Skipped = result.Skipped
          });
        }

        Guid id;
        if (!Guid.TryParse(s[1], out id)) {
          throw new ApiException(404, "not_found", "No such server.");
        }
        var server = context.Manager.Find(id);
        if (server == null) {
          throw new ApiException(404, "not_found", "No such server.");
        }

        if (s.Length == 2) {
          switch (m) {
          case "GET":
            return Ok(ToDto(server));
          case "PUT":
            return UpdateServer(request, server);
          case "DELETE":
            RequireManaged();
            context.Manager.Remove(id);
            return AdminResponse.Empty(204);
          default:
            return MethodNotAllowed();
          }
        }

        if (s.Length == 3 && m == "POST") {
          return ServerAction(server, s[2]);
        }
      }

      throw new ApiException(404, "not_found", "No such endpoint.");
    }

    private AdminResponse ServerAction(ManagedServer server, string action)
    {
      var id = server.Id;
      switch (action) {
      case "start":
        if (server.State == ServerState.Loading) {
          throw new ApiException(409, "conflict", "The server is starting.");
        }
        if (server.IsRunning) {
          throw new ApiException(409, "conflict",
            "The server is already running.");
        }
        RunInBackground(() => context.Manager.Start(id), "start");
        return Accepted(server);
      case "stop":
        if (!server.IsRunning) {
          throw new ApiException(409, "conflict",
            "The server is already stopped.");
        }
        RunInBackground(() => context.Manager.Stop(id), "stop");
        return Accepted(server);
      case "rescan":
        if (!server.IsRunning) {
          throw new ApiException(409, "conflict",
            "The server is not running.");
        }
        RunInBackground(() => context.Manager.Rescan(id), "rescan");
        return Accepted(server);
      default:
        throw new ApiException(404, "not_found", "No such action.");
      }
    }

    /// <summary>
    ///   Start, stop and rescan walk the whole library, so they answer 202 and
    ///   report progress through /events instead of holding the request open.
    /// </summary>
    private static void RunInBackground(Action action, string what)
    {
      Task.Run(() =>
      {
        try {
          action();
        }
        catch (Exception ex) {
          log.Error($"Background {what} failed", ex);
        }
      });
    }

    private AdminResponse CreateServer(AdminRequest request)
    {
      RequireManaged();
      var input = Parse<ServerInputDto>(request);
      var errors = Validation.Validate(input);
      if (errors.Count != 0) {
        throw new ApiException(422, "validation_failed",
          "The server description is not valid.", errors);
      }
      var description = Validation.ToDescription(input, new ServerDescription());
      // Newly created servers start immediately, which is what pressing New in
      // the old GUI effectively did once you pressed Start.
      description.Active = true;
      var server = context.Manager.Add(description);
      var response = Ok(ToDto(server), 201);
      response.Headers["Location"] = $"{PREFIX}/servers/{server.Id}";
      return response;
    }

    private AdminResponse UpdateServer(AdminRequest request,
      ManagedServer server)
    {
      RequireManaged();
      var input = Parse<ServerInputDto>(request);
      var errors = Validation.Validate(input);
      if (errors.Count != 0) {
        throw new ApiException(422, "validation_failed",
          "The server description is not valid.", errors);
      }
      var description =
        Validation.ToDescription(input, server.Description.Clone());
      context.Manager.Update(server.Id, description);
      return Ok(ToDto(server));
    }

    private AdminResponse UpdateSettings(AdminRequest request)
    {
      RequireManaged();
      var input = Parse<SettingsDto>(request);
      var errors = new List<FieldError>();
      if (input.Port < 0 || input.Port > 65535) {
        errors.Add(new FieldError("port", "Must be between 0 and 65535"));
      }
      if (input.RescanDelaySeconds < 1 || input.RescanDelaySeconds > 3600) {
        errors.Add(new FieldError(
          "rescanDelaySeconds", "Must be between 1 and 3600"));
      }
      if (input.RescanIntervalMinutes < 0 ||
          input.RescanIntervalMinutes > 1440) {
        errors.Add(new FieldError(
          "rescanIntervalMinutes", "Must be between 0 and 1440"));
      }
      if (Array.IndexOf(AppSettings.LogLevels, input.LogLevel) < 0) {
        errors.Add(new FieldError("logLevel",
          "Must be one of " + string.Join(", ", AppSettings.LogLevels)));
      }
      if (errors.Count != 0) {
        throw new ApiException(422, "validation_failed",
          "The settings are not valid.", errors);
      }

      var settings = context.Settings.Current;
      var before = settings.Clone();
      settings.Port = input.Port;
      settings.CacheDir = input.CacheDir ?? string.Empty;
      settings.RescanDelaySeconds = input.RescanDelaySeconds;
      settings.RescanIntervalMinutes = input.RescanIntervalMinutes;
      settings.LogLevel = input.LogLevel;
      settings.PreventSleep = input.PreventSleep;
      if (input.StartMinimized.HasValue) {
        settings.StartMinimized = input.StartMinimized.Value;
      }
      context.Settings.Save(settings);

      if (input.Autostart.HasValue && context.SetAutostart != null) {
        try {
          context.SetAutostart(input.Autostart.Value);
        }
        catch (Exception ex) {
          log.Error("Failed to update the autostart entry", ex);
        }
      }

      var restart = new List<string>();
      if (before.Port != settings.Port) {
        restart.Add("port");
      }
      if (!string.Equals(before.CacheDir ?? string.Empty,
        settings.CacheDir ?? string.Empty,
        StringComparison.OrdinalIgnoreCase)) {
        restart.Add("cacheDir");
      }
      return Ok(BuildSettings(restart.ToArray()));
    }

    private void RequireManaged()
    {
      if (!context.Managed) {
        throw new ApiException(409, "cli_managed",
          "This server is configured from the command line. Restart it with " +
          "--managed to manage servers through the API.");
      }
    }

    private StatusDto BuildStatus()
    {
      var settings = context.Settings.Current;
      var cacheDir = Paths.ResolveCacheDir(settings.CacheDir);
      var servers = context.Manager.Servers;
      var monitor = context.Http?.Playback;
      var session = monitor?.Current;
      var playing = monitor != null && monitor.IsPlaying && session != null;
      return new StatusDto
      {
        Version = ProductInformation.ProductVersion,
        Signature = HttpServer.Signature,
        MediaPort = context.Http?.RealPort ?? 0,
        AdminPort = context.AdminPort,
        StartedUtc = Iso(context.StartedUtc),
        CacheDir = cacheDir,
        ConfigDir = Paths.DataDir,
        BrowseUrl = $"http://localhost:{context.Http?.RealPort ?? 0}/",
        Host = context.HostKind,
        Managed = context.Managed,
        Playback = playing
          ? new PlaybackDto
          {
            Playing = true,
            Title = session.Title,
            Client = session.Client?.ToString(),
            MediaType = session.MediaType.ToString().ToLowerInvariant(),
            StartedUtc = Iso(session.Started)
          }
          : null,
        ServerCount = new ServerCountsDto
        {
          Total = servers.Count,
          Running = servers.Count(x => x.IsRunning)
        }
      };
    }

    private static CapabilitiesDto BuildCapabilities()
    {
      var orders = ComparerRepository.ListItems()
        .OrderBy(i => i.Key)
        .Select(i => new NamedItemDto
        {
          Name = i.Value.Name,
          Description = i.Value.Description,
          Default = i.Value.Name == "title"
        }).ToList();
      var views = ViewRepository.ListItems()
        .OrderBy(i => i.Key)
        .Select(i => new NamedItemDto
        {
          Name = i.Value.Name,
          Description = i.Value.Description,
          Configurable = ViewParameters.Describe(i.Value.Name) != null,
          Parameters = ViewParameters.Describe(i.Value.Name)
        }).ToList();
      return new CapabilitiesDto
      {
        Orders = orders,
        Views = views,
        MediaTypes = new List<string> {"video", "audio", "image"},
        RestrictionTypes = new List<string> {"mac", "ip", "userAgent"},
        LogLevels = AppSettings.LogLevels.ToList()
      };
    }

    private SettingsDto BuildSettings(string[] restartRequired)
    {
      var s = context.Settings.Current;
      var tray = context.HostKind == "tray";
      return new SettingsDto
      {
        Port = s.Port,
        CacheDir = s.CacheDir ?? string.Empty,
        RescanDelaySeconds = s.RescanDelaySeconds,
        RescanIntervalMinutes = s.RescanIntervalMinutes,
        LogLevel = s.LogLevel,
        StartMinimized = tray ? (bool?)s.StartMinimized : null,
        PreventSleep = s.PreventSleep,
        Autostart = tray && context.GetAutostart != null
          ? (bool?)SafeAutostart()
          : null,
        Effective = new EffectiveSettingsDto
        {
          Port = context.Http?.RealPort ?? 0,
          CacheDir = Paths.ResolveCacheDir(s.CacheDir)
        },
        RestartRequired = restartRequired
      };
    }

    private bool SafeAutostart()
    {
      try {
        return context.GetAutostart();
      }
      catch (Exception) {
        return false;
      }
    }

    private LogDto ReadLog(AdminRequest request)
    {
      var settings = context.Settings.Current;
      if (settings.LogLevel == "None") {
        return new LogDto
        {
          Disabled = true,
          Level = request.QueryValue("level"),
          Lines = new List<LogLineDto>()
        };
      }
      var cacheDir = Paths.ResolveCacheDir(settings.CacheDir);
      return LogReader.Read(
        Paths.LogFile(cacheDir),
        request.QueryInt("tail", 200),
        request.QueryValue("level"));
    }

    private AdminResponse BrowseFs(AdminRequest request)
    {
      var path = request.QueryValue("path");
      FsDto rv;
      try {
        rv = FileSystemBrowser.List(path);
      }
      catch (ArgumentException) {
        throw new ApiException(400, "bad_parameter", "Malformed path.");
      }
      catch (NotSupportedException) {
        throw new ApiException(400, "bad_parameter", "Malformed path.");
      }
      if (rv == null) {
        throw new ApiException(404, "not_found", "No such directory.");
      }
      return Ok(rv);
    }

    /// <summary>
    ///   Server-Sent Events. Every event is a nudge to refetch, so a client
    ///   that misses one is not left inconsistent.
    /// </summary>
    private AdminResponse Events()
    {
      var response = AdminResponse.Stream("text/event-stream", stream =>
      {
        using (var subscriber = context.Events.Subscribe()) {
          var writer = new StreamWriter(stream, new UTF8Encoding(false))
          {
            AutoFlush = true
          };
          writer.Write("retry: 3000\n\n");
          var token = CancellationToken.None;
          while (true) {
            var payload = subscriber.Take(TimeSpan.FromSeconds(20), token);
            // Null means the wait timed out: emit a keepalive so the client can
            // tell a live-but-quiet server from a dead one.
            writer.Write(payload ?? "event: ping\ndata: {}\n\n");
          }
        }
      });
      response.Headers["Cache-Control"] = "no-store";
      response.Headers["X-Accel-Buffering"] = "no";
      return response;
    }

    private ServerDto ToDto(ManagedServer server)
    {
      var d = server.Description;
      return new ServerDto
      {
        Id = d.Id.ToString(),
        Name = d.Name,
        Active = d.Active,
        State = server.State.ToString().ToLowerInvariant(),
        LastError = server.LastError,
        Order = d.Order,
        OrderDescending = d.OrderDescending,
        Types = Validation.TypesToArray(d.Types),
        Views = d.Views ?? new string[0],
        Directories = d.Directories ?? new string[0],
        Restrictions = new RestrictionsDto
        {
          Mac = d.Macs ?? new string[0],
          Ip = d.Ips ?? new string[0],
          UserAgent = d.UserAgents ?? new string[0]
        },
        Uuid = server.Uuid?.ToString(),
        MountPrefix = server.MountPrefix,
        StartedUtc = server.StartedUtc.HasValue
          ? Iso(server.StartedUtc.Value)
          : null,
        LoadSeconds = server.LoadSeconds
      };
    }

    private static string Iso(DateTime value)
    {
      return value.ToUniversalTime()
        .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static T Parse<T>(AdminRequest request) where T : class
    {
      if (request.BodyBytes.Length == 0) {
        throw new ApiException(400, "bad_json", "A JSON body is required.");
      }
      try {
        var rv = JsonSerializer.Deserialize<T>(request.Body, JsonOptions);
        if (rv == null) {
          throw new ApiException(400, "bad_json", "A JSON body is required.");
        }
        return rv;
      }
      catch (JsonException ex) {
        throw new ApiException(400, "bad_json", "Malformed JSON: " + ex.Message);
      }
    }

    private static AdminResponse Get(string method, Func<AdminResponse> handler)
    {
      return method == "GET" ? handler() : MethodNotAllowed();
    }

    private static AdminResponse MethodNotAllowed()
    {
      return Error(405, "method_not_allowed",
        "That method is not allowed here.");
    }

    private AdminResponse Accepted(ManagedServer server)
    {
      return Ok(ToDto(server), 202);
    }

    private static AdminResponse Ok(object payload, int status = 200)
    {
      var response = AdminResponse.Json(
        status, JsonSerializer.Serialize(payload, JsonOptions));
      response.Headers["Cache-Control"] = "no-store";
      return response;
    }

    internal static AdminResponse Error(int status, string code,
      string message, List<FieldError> details = null)
    {
      var payload = new ErrorEnvelope
      {
        Error = new ErrorBody
        {
          Code = code,
          Message = message,
          Details = details
        }
      };
      var response = AdminResponse.Json(
        status, JsonSerializer.Serialize(payload, JsonOptions));
      response.Headers["Cache-Control"] = "no-store";
      return response;
    }
  }

  internal sealed class ApiException : Exception
  {
    public ApiException(int status, string code, string message,
      List<FieldError> details = null)
      : base(message)
    {
      Status = status;
      Code = code;
      Details = details;
    }

    public int Status { get; }

    public string Code { get; }

    public List<FieldError> Details { get; }
  }
}

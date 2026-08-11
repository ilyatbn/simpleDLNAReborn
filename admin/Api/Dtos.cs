using System.Collections.Generic;

namespace NMaier.SimpleDlna.Admin.Api
{
  public sealed class ErrorEnvelope
  {
    public ErrorBody Error { get; set; }
  }

  public sealed class ErrorBody
  {
    public string Code { get; set; }

    public string Message { get; set; }

    public List<FieldError> Details { get; set; }
  }

  public sealed class FieldError
  {
    public FieldError()
    {
    }

    public FieldError(string field, string message)
    {
      Field = field;
      Message = message;
    }

    public string Field { get; set; }

    public string Message { get; set; }
  }

  public sealed class PlaybackDto
  {
    public bool Playing { get; set; }

    public string Title { get; set; }

    public string Client { get; set; }

    public string MediaType { get; set; }

    public string StartedUtc { get; set; }
  }

  public sealed class StatusDto
  {
    public string Version { get; set; }

    public string Signature { get; set; }

    public int MediaPort { get; set; }

    public int AdminPort { get; set; }

    public string StartedUtc { get; set; }

    public string CacheDir { get; set; }

    public string ConfigDir { get; set; }

    public string BrowseUrl { get; set; }

    /// <summary>"tray" or "console".</summary>
    public string Host { get; set; }

    /// <summary>
    ///   False when servers come from the command line, in which case every
    ///   mutating endpoint answers 409.
    /// </summary>
    public bool Managed { get; set; }

    public PlaybackDto Playback { get; set; }

    public ServerCountsDto ServerCount { get; set; }
  }

  public sealed class ServerCountsDto
  {
    public int Total { get; set; }

    public int Running { get; set; }
  }

  public sealed class NamedItemDto
  {
    public string Name { get; set; }

    public string Description { get; set; }

    public bool Default { get; set; }

    public bool Configurable { get; set; }

    public List<ViewParameterDto> Parameters { get; set; }
  }

  public sealed class ViewParameterDto
  {
    public ViewParameterDto()
    {
    }

    public ViewParameterDto(string name, string type, string unit,
      string @default, string description)
    {
      Name = name;
      Type = type;
      Unit = unit;
      Default = @default;
      Description = description;
    }

    public string Name { get; set; }

    public string Type { get; set; }

    public string Unit { get; set; }

    public string Default { get; set; }

    public string Description { get; set; }
  }

  public sealed class CapabilitiesDto
  {
    public List<NamedItemDto> Orders { get; set; }

    public List<NamedItemDto> Views { get; set; }

    public List<string> MediaTypes { get; set; }

    public List<string> RestrictionTypes { get; set; }

    public List<string> LogLevels { get; set; }
  }

  public sealed class RestrictionsDto
  {
    public string[] Mac { get; set; } = new string[0];

    public string[] Ip { get; set; } = new string[0];

    public string[] UserAgent { get; set; } = new string[0];
  }

  public sealed class ServerDto
  {
    public string Id { get; set; }

    public string Name { get; set; }

    public bool Active { get; set; }

    public string State { get; set; }

    public string LastError { get; set; }

    public string Order { get; set; }

    public bool OrderDescending { get; set; }

    public string[] Types { get; set; }

    public string[] Views { get; set; }

    public string[] Directories { get; set; }

    public RestrictionsDto Restrictions { get; set; }

    public string Uuid { get; set; }

    public string MountPrefix { get; set; }

    public string StartedUtc { get; set; }

    public double? LoadSeconds { get; set; }
  }

  public sealed class ServerListDto
  {
    public List<ServerDto> Servers { get; set; }
  }

  /// <summary>
  ///   The writable shape of a server. Separate from <see cref="ServerDto" />
  ///   so read-only fields cannot be smuggled in through a PUT.
  /// </summary>
  public sealed class ServerInputDto
  {
    public string Name { get; set; }

    public string Order { get; set; }

    public bool OrderDescending { get; set; }

    public string[] Types { get; set; }

    public string[] Views { get; set; }

    public string[] Directories { get; set; }

    public RestrictionsDto Restrictions { get; set; }
  }

  public sealed class RescanAllDto
  {
    public int Requested { get; set; }

    public int Skipped { get; set; }
  }

  public sealed class SettingsDto
  {
    public int Port { get; set; }

    public string CacheDir { get; set; }

    public int RescanDelaySeconds { get; set; }

    public int RescanIntervalMinutes { get; set; }

    public string LogLevel { get; set; }

    public bool? StartMinimized { get; set; }

    public bool PreventSleep { get; set; }

    public bool? Autostart { get; set; }

    public EffectiveSettingsDto Effective { get; set; }

    public string[] RestartRequired { get; set; }
  }

  public sealed class EffectiveSettingsDto
  {
    public int Port { get; set; }

    public string CacheDir { get; set; }
  }

  public sealed class LogLineDto
  {
    public string Timestamp { get; set; }

    public string Level { get; set; }

    public string Logger { get; set; }

    public string Message { get; set; }
  }

  public sealed class LogDto
  {
    public string Path { get; set; }

    public string Level { get; set; }

    public bool Disabled { get; set; }

    public long TotalBytes { get; set; }

    public List<LogLineDto> Lines { get; set; }
  }

  public sealed class FsEntryDto
  {
    public string Name { get; set; }

    public string Path { get; set; }

    public bool HasChildren { get; set; }

    public bool Accessible { get; set; }
  }

  public sealed class FsDto
  {
    public string Path { get; set; }

    public string Parent { get; set; }

    public List<FsEntryDto> Entries { get; set; }
  }
}

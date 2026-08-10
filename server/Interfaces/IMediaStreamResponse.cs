namespace NMaier.SimpleDlna.Server
{
  /// <summary>
  ///   A response that carries media content, letting the HTTP layer tell a
  ///   real playback stream apart from a cover or a subtitle fetch.
  /// </summary>
  internal interface IMediaStreamResponse : IResponse
  {
    IMediaResource MediaItem { get; }

    /// <summary>
    ///   True when this transfer represents something being played, rather than
    ///   an artwork or subtitle side-fetch.
    /// </summary>
    bool IsPlayback { get; }
  }
}

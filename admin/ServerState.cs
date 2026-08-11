namespace NMaier.SimpleDlna.Admin
{
  /// <summary>
  ///   Lifecycle state of a managed server.
  /// </summary>
  /// <remarks>
  ///   The numeric values match the image order the tray GUI used, and the
  ///   names are what the REST API reports (lowercased).
  /// </remarks>
  public enum ServerState
  {
    Idle = 0,
    Running = 1,
    Stopped = 2,
    Refreshing = 3,
    Loading = 4
  }
}

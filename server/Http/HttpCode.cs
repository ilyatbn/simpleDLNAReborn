namespace NMaier.SimpleDlna.Server
{
  public enum HttpCode
  {
    None = 0,
    Ok = 200,
    Partial = 206,
    MovedPermanently = 301,
    NotModified = 304,
    TemporaryRedirect = 307,
    Denied = 403,
    NotFound = 404,
    // GENA: tells a control point its SID is unknown so it re-subscribes.
    PreconditionFailed = 412,
    RangeNotSatisfiable = 416,
    InternalError = 500
  }
}

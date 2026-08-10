# util — SimpleDlna.Utilities

Bottom of the dependency stack. Everything else references this; it references
nothing in the repo. Keep it that way.

## Shortcuts

| Need | File |
| --- | --- |
| Base class that gives a type `Debug`/`InfoFormat`/`Error` etc. | `Logging.cs` |
| Open/pool the metadata SQLite connection | `Sqlite.cs` |
| Copy one stream into another asynchronously | `StreamPump.cs` |
| Pooled `MemoryStream`s (RecyclableMemoryStream) | `StreamManager.cs` |
| Shell out to ffmpeg / probe for it on PATH | `Ffmpeg.cs` |
| Seekable HTTP-backed stream | `HttpStream.cs` |
| Local IPs, MAC lookup | `IP.cs`, `AddressToMacResolver.cs` |
| "Natural" (human) string sorting used by all comparers | `NaturalStringComparer.cs`, `*SortPart.cs` |
| File size / duration formatting, title stemming | `Formatting.cs` |
| LRU cache used for covers and thumbnails | `LeastRecentlyUsedDictionary.cs` |
| Assembly title/version/copyright readback | `ProductInformation.cs` |

## Gotchas

- `StreamPump.Finish` used delegate `BeginInvoke`, which throws
  `PlatformNotSupportedException` on modern .NET. It now queues to the thread
  pool. Do not "simplify" it back to a direct call — the callback must not run
  on the I/O completion thread, and it must not block `sem.Release()`.
- `Sqlite.cs` still carries a reflection-based Mono.Data.Sqlite path guarded by
  `SystemInformation.IsRunningOnMono()`. It is dead code under .NET 10 and can
  go whenever someone is confident nobody runs this under Mono.
- `HttpStream.cs` uses `WebRequest`, which is obsolete (SYSLIB0014, suppressed
  repo-wide). Still functional; a rewrite onto `HttpClient` is a real change,
  not a mechanical one, because the class depends on synchronous seek/read.
- This project is the one place that should stay free of `System.Windows.Forms`.
  `Ffmpeg.cs` does pull in `System.Drawing` for `Size`, which is what forces the
  `-windows` TFM down the whole chain.

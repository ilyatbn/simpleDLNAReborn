# util — SimpleDlna.Utilities

Bottom of the dependency stack. Everything else references this; it references
nothing in the repo. Keep it that way.

## Shortcuts

| Need | File |
| --- | --- |
| Base class that gives a type `Debug`/`InfoFormat`/`Error` etc. | `Logging.cs` |
| Open/pool the metadata SQLite connection | `Sqlite.cs` |
| Copy one stream into another asynchronously | `StreamPump.cs` |
| Cap a stream at N bytes (bounded HTTP ranges) | `LimitedStream.cs` |
| Read several streams as one (headers + body) | `ConcatenatedStream.cs` |
| Detect a network change (IP / gateway / SSID) | `NetworkMonitor.cs`, `Wlan.cs` |
| Pooled `MemoryStream`s (RecyclableMemoryStream) | `StreamManager.cs` |
| Shell out to ffmpeg / probe for it on PATH | `Ffmpeg.cs` |
| Seekable HTTP-backed stream | `HttpStream.cs` |
| Local IPs, MAC lookup | `IP.cs`, `AddressToMacResolver.cs` |
| "Natural" (human) string sorting used by all comparers | `NaturalStringComparer.cs`, `*SortPart.cs` |
| File size / duration formatting, title stemming | `Formatting.cs` |
| LRU cache used for covers and thumbnails | `LeastRecentlyUsedDictionary.cs` |
| Assembly title/version/copyright readback | `ProductInformation.cs` |
| Keep the machine awake | `SleepInhibitor.cs` |

## The stream trio (2026-09)

`StreamPump` + `ConcatenatedStream` + `LimitedStream` are what an HTTP response
body travels through. Three things about them are deliberate:

- **`StreamPump` is double buffered.** It reads the next block while writing
  the current one. It used to be strictly serial, so the disk idled for the
  whole of every socket write. Measured 550 → 1290 MB/s over loopback.
- **It uses `ReadAsync`, not `BeginRead`.** A stream that does not override
  `BeginRead` gets the default, which runs synchronous `Read` on a pool thread
  — and `FileReadStream` is opened `FileOptions.Asynchronous`, where a
  synchronous read is the documented slow path. So `ConcatenatedStream`
  overrides `ReadAsync`; any stream added to the chain should too.
- **`LimitedStream` is not optional.** The pump copies until EOF, so whatever
  it is handed *is* the response body. A bounded `Range` is only bounded
  because `HttpClient.ProcessRanges` wraps the body in one.

`ConcatenatedStream.Advance` closes the exhausted stream as well as disposing
it. `FileReadStream.Close()` is what recycles a handle into `FileStreamCache`;
disposing alone leaked it.

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
- `SleepInhibitor` owns a dedicated thread on purpose: `SetThreadExecutionState`
  is *thread scoped*, so asserting it from a thread-pool thread silently stops
  working when that thread is recycled. Do not "simplify" it to a direct call.
- This project is the one place that should stay free of `System.Windows.Forms`.
  `Ffmpeg.cs` does pull in `System.Drawing` for `Size`, which is what forces the
  `-windows` TFM down the whole chain.

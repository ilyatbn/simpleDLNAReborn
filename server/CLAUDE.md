# server — SimpleDlna.Server

The DLNA/UPnP protocol layer: a hand-rolled HTTP server, SSDP discovery, the
SOAP ContentDirectory service, and the browser UI. Knows nothing about the
filesystem — media comes in through the `IMediaServer` / `IMediaItem`
interfaces, which `fsserver/` implements.

Largest project in the repo (~95 files), so navigate by folder:

| Folder | Contents |
| --- | --- |
| `Http/` | Socket accept loop, request parsing, response writing, auth |
| `Ssdp/` | UPnP discovery: alive/byebye notifications, M-SEARCH replies |
| `Handlers/` | Things bound to a URL prefix — the media mount, icons, static files |
| `Responses/` | `IResponse` implementations (file, item, string, redirect) |
| `Types/` | DLNA enums/mappings, headers, exceptions, virtual folders |
| `Views/` | Optional re-organizations of the media tree (`--view`) |
| `Comparers/` | Sort orders (`--sort`) |
| `Interfaces/` | The contract `fsserver/` implements |
| `Resources/` | UPnP XML templates, `browse.css`, icons — embedded via `Properties/Resources.resx` |

## Shortcuts

- Accept loop and connection lifetime: `Http/HTTPServer.cs`
- Per-connection state machine, request parse, response write: `Http/HttpClient.cs`
- Access control (`--ip` / `--mac` / `--ua`): `Http/HttpAuthorizer.cs` and its `I*Authorizer` siblings
- Device advertisement, M-SEARCH: `Ssdp/SsdpHandler.cs`
- GENA eventing (what makes a TV notice the library changed): `Http/EventSubscriptions.cs`, driven from `MediaMount.ChangedServer`
- "Is anything playing right now": `Types/PlaybackMonitor.cs`, exposed as `HttpServer.Playback`
- `description.xml` and the SOAP endpoints: `Handlers/MediaMount.cs`, `Handlers/MediaMount_SOAP.cs`
- The HTML browse UI: `Handlers/MediaMount_HTML.cs`
- Stable per-item IDs handed out to clients: `Types/Identifiers.cs`
- MIME/profile tables (`DLNA.ORG_PN`, etc.): `Types/DlnaMaps.cs`, `Types/DlnaMime.cs`
- Registering a new view or sort order: `Views/ViewRepository.cs`, `Comparers/ComparerRepository.cs` — both discover implementations by reflection, so adding a class is enough

## Playback detection

`HttpServer.Playback` is a shared `PlaybackMonitor` covering every mount. New
consumers just subscribe to `Changed` or read `IsPlaying`/`Current`; nothing
needs to be added to the HTTP layer.

A transfer counts as playback only when the response implements
`IMediaStreamResponse` and reports `IsPlayback` — `ItemResponse` requires
transferMode `Streaming` (covers are `Interactive`, subtitles `Background`) and
an Audio or Video media type, so artwork fetches and photo slideshows do not
count. `HttpClient.StartPlayback` opens the session and the `StreamPump`
callback closes it, so aborted transfers end correctly too.

Players issue many short range requests rather than one long read, so the
monitor stays "playing" for `Grace` (15s) after the last stream closes. Without
that it would flip state several times a minute. Transitions are logged at Info;
individual streams only at Debug.

## Device identity — do not randomise it

`HttpServer.DeviceGuid(server, address)` derives the UPnP uuid from the server's
own UUID XORed with the address. It must stay deterministic. A control point
keys its device list on that uuid, so a fresh one is a **new entry on the TV**,
not a refresh — and the stale one lingers for the advertised `max-age` (600s).
It was `Guid.NewGuid()`, which meant every restart, and every `Readvertise()`
after a network change, left another copy of the server in the TV's list.

`FileServer.DeriveUUID` is stable for the same reason: its tail used to come
from `Guid.NewGuid()`, so a short friendly name left random bytes in it.

## Range requests and connection reuse (2026-09)

All in `Http/HttpClient.cs`. A scrubbing TV sends a couple of ranged GETs a
second, so this path is the one that decides how seeking feels.

- **`ProcessRanges` caps the body.** It seeks to `start` and wraps the result in
  a `LimitedStream`. Without the wrapper the pump runs to EOF: a
  `Range: bytes=1000-50999` answered `Content-Length: 50000` and then sent
  200 MB. That also desynchronises the connection, which is why keep-alive was
  impossible.
- **Headers go into a per-request copy, never onto the response.** Handlers may
  hand back a response object shared across connections — `HttpServer`
  registers one `ResourceResponse` for `/favicon.ico` and keeps it forever.
  Writing `Content-Length` onto that from two threads is a torn dictionary.
  `Error()` builds a fresh response for the same reason.
- **Supported range forms**: `N-M`, `N-`, `-N` (suffix). `start == end` is a
  legal single byte and a common container probe. Only a start past the end, or
  an inverted range, is a 416 — and a 416 must carry status, body *and*
  `Content-Range: bytes */total`. Multi-ranges are not parsed; the whole
  representation is a legal answer and is what they get.
- **Keep-alive follows the protocol version.** HTTP/1.1 is persistent unless
  the client says `Connection: close`; HTTP/1.0 needs an explicit
  `Connection: keep-alive`. The old code required the explicit header either
  way while `ResponseHeaders` announced `keep-alive` on every response, so
  clients were promised reuse and then cut off.
- **The header parser re-reads `readStream` from offset 0 on every pass**, so
  `Method`, `Path` and `Headers` are cleared at the top of each pass. A request
  split across two TCP segments otherwise reparses its own request line as a
  header and throws. `SetupResponse` only runs once `hasHeaders` is set.
- **A zero-length read is a close**, not an empty read. Falling through left
  `Path` holding the previous request and replayed it without its Range header.
- Pipelined bytes are detected and force `Connection: close`; this parser cannot
  carry them into the next request and dropping them silently would deadlock.

## Gotchas

- Item IDs in `Types/Identifiers.cs` are regenerated per process. A URL captured
  from one run 404s (actually 500s — `GetItemById` throws `KeyNotFoundException`
  straight out) against the next. Expect this when testing by hand.
- `Resources/*` files are pulled into the assembly as `byte[]` through
  `ResXFileRef` in `Properties/Resources.resx`. Adding a resource means editing
  that resx, not just dropping a file in the folder.
- `Types/SubTitle.cs` implements `ISerializable` purely so the `fsserver` cache
  can persist it — see `fsserver/ItemSerializer.cs`. Changing its payload means
  bumping `FileStore.SCHEMA`.
- The `System.Drawing` / `System.Windows.Forms` references this project used to
  carry were vestigial; no code here uses them.
- Each service needs its **own** `eventSubURL` (`events/cds`, `events/cms`,
  `events/mrr`), otherwise an incoming SUBSCRIBE cannot be attributed to a
  service. They all pointed at one URL before eventing was implemented.
- `request.Headers[...]` throws on a missing key. GENA headers are all
  conditional, so go through `MediaMount.Header(request, name)`.
- `EventSubscriptions` sends NOTIFY fire-and-forget and drops a subscription
  once every callback URL fails, so a TV that was unplugged stops being retried
  on every rescan.

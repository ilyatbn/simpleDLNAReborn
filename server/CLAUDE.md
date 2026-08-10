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
- `description.xml` and the SOAP endpoints: `Handlers/MediaMount.cs`, `Handlers/MediaMount_SOAP.cs`
- The HTML browse UI: `Handlers/MediaMount_HTML.cs`
- Stable per-item IDs handed out to clients: `Types/Identifiers.cs`
- MIME/profile tables (`DLNA.ORG_PN`, etc.): `Types/DlnaMaps.cs`, `Types/DlnaMime.cs`
- Registering a new view or sort order: `Views/ViewRepository.cs`, `Comparers/ComparerRepository.cs` — both discover implementations by reflection, so adding a class is enough

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

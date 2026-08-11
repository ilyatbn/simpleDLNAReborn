# simpleDLNA

A zero-config DLNA/UPnP-AV media server. Two front ends over a shared stack:
`sdlna.exe` (console) and `SimpleDLNA.exe` (Windows tray app). Both serve the
same admin web interface on `http://localhost:19199/`, bound to loopback.

## Build

Needs the .NET 10 SDK **and Node.js** — the admin SPA in `web/` is built by npm
and embedded into `SimpleDlna.Admin`.

```
dotnet build sdlna.sln
dotnet build sdlna.sln -p:SkipWebBuild=true   # no Node; API only, no web UI
dotnet run --project sdlna/sdlna.csproj -- --help
```

Or via the `Makefile`, which publishes into `dist/`:

```
make                          # both apps -> dist/console, dist/gui
make run ARGS="--help"
make build SELF_CONTAINED=false   # 6 MB instead of 83 MB, needs the runtime
make zip                      # timestamped zips in dist/
make help
```

`make` is not bundled with Git for Windows — `winget install ezwinports.make`.

No Visual Studio needed — the .NET SDK alone is enough. CI is
`.github/workflows/build-release.yml`, which publishes self-contained win-x64
zips and cuts a release tagged `simpledlna-<UTC timestamp>`.

## Project map

Dependency order — each layer only knows about the ones above it.

| Project | Assembly | What lives there |
| --- | --- | --- |
| `util/` | SimpleDlna.Utilities | Logging, SQLite plumbing, stream pumps, ffmpeg shell-out, sorting |
| `server/` | SimpleDlna.Server | HTTP server, SSDP, SOAP/ContentDirectory, web UI, views, DLNA types |
| `thumbs/` | SimpleDlna.Thumbnails | Thumbnail generation (System.Drawing + ffmpeg) |
| `fsserver/` | SimpleDlna.FileMediaServer | Filesystem scanning, media metadata, the SQLite item cache |
| `admin/` | SimpleDlna.Admin | Server lifecycle, settings, the loopback REST API, the embedded web UI |
| `web/` | *(npm)* | The admin SPA — React + TypeScript + Vite |
| `sdlna/` | sdlna.exe | Console entry point + option parsing |
| `SimpleDLNA/` | SimpleDLNA.exe | Tray app that opens the web interface |

`setup/setup.vdproj` is a dead Visual Studio Installer project — it is not in
the solution and cannot be built without VS. Ignore it.

## Shortcuts — where things actually are

- Admin REST API: `admin/Api/ApiHandler.cs`; its listener in `admin/Http/AdminServer.cs`
- Server lifecycle (start/stop/rescan, descriptors.xml): `admin/ServerManager.cs`
- Admin web UI: `web/src/` — see `web/CLAUDE.md`
- Tray app: `SimpleDLNA/TrayContext.cs`
- Console startup / wiring: `sdlna/Program.cs`, options in `sdlna/Options.cs`
- HTTP request loop: `server/Http/HTTPServer.cs`, `server/Http/HttpClient.cs`
- DLNA device discovery: `server/Ssdp/SsdpHandler.cs`
- SOAP / ContentDirectory browse: `server/Handlers/MediaMount_SOAP.cs`
- Browser UI (the HTML you see at `http://host:port/`): `server/Handlers/MediaMount_HTML.cs` + `server/Resources/browse.css`
- Directory scanning: `fsserver/FileServer.cs`, `fsserver/PlainFolder.cs`
- Per-file metadata (taglib): `fsserver/Files/{Audio,Video,Image}File.cs`
- Metadata cache DB + serialization: `fsserver/FileStore.cs`, `fsserver/ItemSerializer.cs`
- Thumbnail entry point: `thumbs/ThumbnailMaker.cs`

## Conventions

- 2-space indent, Allman-ish braces but `{` on the same line for control flow.
  `.editorconfig` covers the basics.
- Classes needing logging derive from `NMaier.SimpleDlna.Utilities.Logging` and
  call `Debug`/`InfoFormat`/`Error` directly; static contexts use
  `LogManager.GetLogger(typeof (X))`.
- Assembly metadata and the shared TFM live in `Directory.Build.props`, not in
  per-project `AssemblyInfo.cs` files (those were deleted in the .NET 10 move).

## Modernization notes (.NET Framework 4.5.1 → .NET 10)

Things that were load-bearing and are worth not re-breaking:

- **BinaryFormatter is gone.** `fsserver/ItemSerializer.cs` replaces it, keeping
  the `ISerializable`/`SerializationInfo` shape the cached types already had.
  Changing any `GetObjectData` payload means bumping `FileStore.SCHEMA`.
- **Delegate `BeginInvoke` is gone** (it needed remoting). `util/StreamPump.cs`
  queues to the thread pool instead. WinForms `Control.BeginInvoke` is unrelated
  and still fine.
- Targeting `net10.0-windows` is required by WinForms *and* by
  `System.Drawing.Common`, which is Windows-only. Making the console server
  cross-platform means replacing `System.Drawing` in `thumbs/` and `util/Ffmpeg.cs`.

## The web UI migration (2026-08)

The WinForms GUI is gone; `modernization.md` is the design record and
`MIGRATION-PLAN.md` the process that produced it. Worth knowing:

- `modernization.md` §1 is a control-by-control inventory of the old GUI, and
  §3.7 maps every one of its 34 capabilities onto the web UI or an explicit
  drop. Use it before claiming something is missing — or was never there.
- `admin/` has its own HTTP layer rather than reusing `server/Http`. §2.1 says
  why, in eight numbered reasons.
- **`server/Http/HttpClient.cs:259` re-encodes request bodies as ASCII**, which
  corrupts any non-ASCII SOAP request. Known, documented in §2.13, not yet
  fixed — the admin API sidesteps it by not using that parser.

# simpleDLNA

A zero-config DLNA/UPnP-AV media server. Two front ends over a shared stack:
`sdlna.exe` (console) and `SimpleDLNA.exe` (Windows tray GUI).

## Build

```
dotnet build sdlna.sln
dotnet run --project sdlna/sdlna.csproj -- --help
```

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
| `sdlna/` | sdlna.exe | Console entry point + option parsing |
| `SimpleDLNA/` | SimpleDLNA.exe | WinForms tray GUI |
| `NMaier.Windows.Forms/` | NMaier.Windows.Forms | Small WinForms base-class/renderer helpers |

`setup/setup.vdproj` is a dead Visual Studio Installer project — it is not in
the solution and cannot be built without VS. Ignore it.

## Shortcuts — where things actually are

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

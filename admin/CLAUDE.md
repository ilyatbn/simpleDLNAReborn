# admin — SimpleDlna.Admin

Server management plus the loopback admin API and the embedded web UI. The first
project allowed to reference both `server/` and `fsserver/`, which is why the
server lifecycle lives here rather than in either of them.

Both front ends construct the same objects, so the console and the tray app
cannot drift apart.

## Shortcuts

| Need | File |
| --- | --- |
| Own every configured server, persist them | `ServerManager.cs` |
| Start/stop/rescan one server | `ManagedServer.cs` |
| The persisted per-server model | `ServerDescription.cs` |
| descriptors.xml read/write | `DescriptorStore.cs` |
| Global settings + migration | `SettingsStore.cs`, `AppSettings.cs` |
| Where files live | `Paths.cs` |
| Wire everything together | `AdminHost.cs` |
| HTTP listener, request/response | `Http/AdminServer.cs` and siblings |
| Serve the embedded SPA | `Http/WebAssets.cs` |
| The REST endpoints | `Api/ApiHandler.cs` |
| Validation, mirroring the old dialog | `Api/Validation.cs` |
| SSE fan-out | `Api/EventHub.cs` |

## Why its own HTTP layer

`server/Http` serves media and is wrong for a JSON control API in eight ways
documented in `modernization.md` §2.1. The two that matter most:

- It round-trips request bodies through `Encoding.ASCII`
  (`server/Http/HttpClient.cs:259`), so a server named `Фильмы` would be stored
  as `??????`. **This is a live bug over there, not just an API concern** — it
  corrupts non-ASCII SOAP requests too.
- It never splits the query string off the path and never URL-decodes it, and
  its status table throws on any code it does not know.

Fixing those means editing the code path that streams to TVs. This listener is
~370 lines, touches nothing, and is loopback-only.

## Gotchas

- **Loopback binding is the entire security model.** No auth, no tokens. If the
  bind address ever widens, auth is a prerequisite, not a follow-up.
- `ServerManager.Persist` must be false whenever servers were adopted rather
  than loaded, or the console would overwrite the tray app's `descriptors.xml`
  with its command-line servers.
- Configuration is written to `Paths.DataDir`, never the configurable cache
  directory. It used to follow the cache setting, so changing that setting made
  every configured server disappear.
- `ManagedServer` deliberately reproduces the old GUI's semantics, including
  flipping a server back to inactive when it fails to start. `modernization.md`
  §1.9 is the reference.
- The SPA is embedded from `web/dist` by targets in `admin.csproj`. The
  `EmbeddedResource` items are added **inside a target hooked to
  `AssignTargetPaths`** — see the comment there before changing it, because the
  obvious alternative silently embeds nothing and still builds green.

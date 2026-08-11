# WinForms → Web Admin UI: migration design plan

> This document is the **plan for the migration process** — the four-step,
> approval-gated sequence that produces `modernization.md`. It mandates no code
> changes; each step is a documentation pass, approved and committed on its own.
>
> `modernization.md` is reserved for the four deliverable sections §1–§4.
> This file is the durable record that lets the work be picked up later.

## Progress

- [x] **Step 0** — this plan → `MIGRATION-PLAN.md`
- [ ] **Step 1** — GUI feature inventory → `modernization.md` §1
- [ ] **Step 2** — REST API design → `modernization.md` §2
- [ ] **Step 3** — SPA design → `modernization.md` §3
- [ ] **Step 4** — WinForms deprecation + build integration → `modernization.md` §4

---

## 1. Context

simpleDLNA has two front ends over one shared stack: `sdlna.exe` (console) and
`SimpleDLNA.exe` (WinForms tray GUI). The GUI is the only interactive way to
manage the server — it owns the server list, the add/edit dialog, global
settings, autostart, the tray icon and the playback status bar. That ties
interactive use to Windows + WinForms and blocks the goal already written into
`TODO.md`:

```
- move away from the c# webforms
    - webserver
    - rest api for everything in the gui
    - modern web ui.
```

**Intended outcome:** a REST API at `http://localhost:19199/api/v1` hosted inside
the existing .NET process, a React SPA served from the same port with feature
parity with the GUI, and `SimpleDLNA.exe` reduced to a tray icon that opens the
browser.

**Scope of this phase: documentation only.** Everything lands in
`modernization.md`, written in four separately-approved, separately-committed
passes. Implementation is a later phase, gated separately.

---

## 2. Decisions already made

| Question | Decision | Consequence |
| --- | --- | --- |
| Admin hosting | A **second listener bound to `127.0.0.1:19199`** in the same process, serving `/api/v1/*` and the SPA | The DLNA `HttpServer` keeps binding `IPAddress.Any` on its configurable/ephemeral port; the admin surface is never LAN-reachable, and loopback-only binding *is* the v1 auth model |
| SPA stack | **React + TypeScript + Vite** | Adds a Node dependency to the build; needs a `SkipWebBuild` escape hatch |
| Tray host | **Strip `SimpleDLNA.exe` to a tray-only shell** | Keeps the project and its `net10.0-windows` TFM, the single-instance mutex/pipe, autostart and sleep-inhibit; deletes all four Forms |
| SPA embedding | **`<EmbeddedResource Include="wwwroot\**" />`** read via `Assembly.GetManifestResourceStream` | Not the hand-maintained `.resx` `ResXFileRef` list used today, which cannot cope with Vite's hashed filenames |

---

## 3. Findings that shape the design

Established by reading the tree; each one changes what the steps below must say.

**The GUI owns logic the API needs.** Start/stop/reconfigure lives in
`SimpleDLNA/ServerListViewItem.cs:75-195`, mixed into a `ListViewItem`. It builds
`Identifiers` from `ComparerRepository.Lookup` + `Identifiers.AddView`, filters
directories, constructs `FileServer`, wires `Changing`/`Changed`, assembles an
`HttpAuthorizer` from the IP/MAC/UA lists, and calls
`HttpServer.RegisterMediaServer`. All of that must become UI-free.

**Project layering blocks the obvious home for it.** Dependency order is
`util → server → fsserver`, so `server/` cannot reference `FileServer`. The
extracted manager therefore needs a new project that references both.

**The registration surface is `internal`.** `IHandler`, `IPrefixHandler`,
`IResponse` and `HttpServer.RegisterHandler` are all internal to
`SimpleDlna.Server` (`server/Interfaces/IHandler.cs:3`,
`server/Http/HTTPServer.cs:216`). A handler defined outside the assembly needs
them public or an `InternalsVisibleTo`.

**Servers have no stable identity.** `ServerDescription`
(`SimpleDLNA/ServerDescription.cs:14-32`) is keyed by `Name` in practice. A REST
resource needs an id.

**The hand-rolled HTTP stack has hard edges**, every one of which the API spec
must answer explicitly:

| Constraint | Where | Impact |
| --- | --- | --- |
| `Path` is the raw request target — never URL-decoded, never split on `?` | `server/Http/HttpClient.cs:265-270` | The API handler must parse and decode the query string itself |
| Request bodies capped at 1 MiB, only read when `Content-Length` is present; no chunked encoding | `HttpClient.cs:254-262` | Fine for admin JSON; must be stated |
| `HttpCode` lacks `201/204/400/401/409/422`, and `HttpPhrases.Phrases[status]` is indexed unconditionally | `server/Http/HttpCode.cs:3-17`, `HttpClient.cs:319-323` | An unlisted code **throws**; both tables must be extended |
| Responses go through `ConcatenatedStream` + `StreamPump` with a computed `Content-Length` | `HttpClient.cs:312-369` | Open-ended SSE does not fit today |
| `FindHandler` is an unordered `StartsWith` scan; `/` is special-cased | `HTTPServer.cs:201-214` | `/api/v1/` and the SPA root must not overlap ambiguously |
| No JSON anywhere in the repo | verified repo-wide | Use in-box `System.Text.Json`; no `PackageReference` needed on `net10.0` |
| Zero custom MSBuild targets exist | all `.csproj`/`.props` | The npm step is greenfield wiring |
| `ResponseHeaders` stamps `Cache-Control: no-cache` on nearly everything | `server/Http/ResponseHeaders.cs:12-20` | Hashed SPA assets want the opposite |

**`FolderBrowserDialog` has no web equivalent** — a filesystem-listing endpoint is
required, not optional, for the directory picker.

---

## 4. Process

**Step 0 — this plan.** Write this document to `MIGRATION-PLAN.md` at the repo
root and commit it alone. It is not touched again except to tick off completed
steps.

Then four steps. Each step: write that section into `modernization.md` → **stop**
→ wait for approval → `git commit` that section alone. No step begins before the
previous is approved and committed. Each commit stages one file only.

---

### Step 1 — GUI feature inventory → `modernization.md` §1

An exhaustive, verbatim map of every window, control, menu item, dropdown choice
and setting in the WinForms GUI, with `file:line` references, so parity can later
be checked mechanically rather than by memory.

Sources: `SimpleDLNA/FormMain.cs` + `.Designer.cs`, `FormServer.cs`,
`FormSettings.cs`, `FormAbout.cs`, `ServerListViewItem.cs`,
`ServerDescription.cs`, `Settings.cs`, `Properties/Settings.Designer.cs`,
`StartUpUtilities.cs`, `Program.cs`.

Contents:

- **FormMain** — the `listDescriptions` ListView (columns Name / Directories /
  Active; states `Idle | Running | Stopped | Refreshing | Loading`); buttons New,
  Edit, Remove, Start-Stop, Rescan; the list context menu; the `&File` menu
  (New Server, Settings, *Prevent sleep while playing*, Open in Browser, Open Log
  Folder, Drop cache, Hide, Exit) and `Help` menu (Homepage, About); the
  `statusPlayback` status bar fed by `httpServer.Playback.Changed`; the tray menu
  including its **dynamic per-server "Rescan {name}"** items; minimize-to-tray and
  close-to-tray semantics; the global mutex + named-pipe single-instance scheme.
- **FormServer** — Name; Order combo populated from `ComparerRepository.ListItems()`
  (`date`, `size`, `title`); Descending; Types (Video / Audio / Images →
  `DlnaMediaTypes` flags); the Views tab populated from `ViewRepository.ListItems()`
  (`bytitle, dimension, filter, flatten, large, music, new, plain, series, sites`,
  each with its verbatim description); the Restrictions tab (MAC / IP /
  User-Agent, with their validation rules); Directories; and every validator
  message.
- **FormSettings** — port, cache directory, rescan delay, rescan interval, log
  level (`None | Fatal | Error | Warn | Info | Debug`), start minimized, autostart
  (`HKCU\...\Run`, value name `SimpleDLNA`). Record that there is **no Cancel** —
  bindings commit on property change and the registry write is immediate.
- **FormAbout** — product, version, copyright, embedded LICENSE.
- **Persistence** — `descriptors.xml` (`XmlSerializer` over `ServerDescription[]`
  in `CacheDir`, written via a `.tmp` + copy) vs. user-scoped `Properties.Settings`
  (`user.config`) vs. the registry Run key; `CacheDir` resolution
  (`FormMain.cs:131-163`); the one-way migration of the legacy
  `config.Descriptors` list.
- **Lifecycle** — how `StartFileServer` / `StopFileServer` / `Toggle` /
  `UpdateInfo` / `Rescan` build `Identifiers` and `HttpAuthorizer` and call
  `RegisterMediaServer` / `UnregisterMediaServer`; that editing a server restarts
  it; that a failed start auto-flips the server back to inactive.
- **Known gaps — recorded, not carried forward**:
  - `buttonViewUp` / `buttonViewDown` have no Click handlers
    (`FormServer.Designer.cs:311-329`) — view reordering does not actually exist.
  - The GUI cannot express parameterised views (`large:size=700`) even though
    `util/Repository.cs:52-81` supports it and `FormServer`'s edit ctor
    (`FormServer.cs:51`) *throws* on a stored parameterised name.
  - `config.cache` is treated as a directory in `CacheDir` but as a file path for
    `sdlna.cache` (`FormMain.cs:67-69`).
  - `LoadConfig` calls `config.Descriptors.Clear()` with no null check
    (`FormMain.cs:400`).
  - `homepageToolStripMenuItem_Click` uses bare `Process.Start(url)` without
    `UseShellExecute` — broken on .NET 5+ (`FormMain.cs:346`).
  - No balloon tips / toasts exist anywhere.

---

### Step 2 — REST API design → `modernization.md` §2

The full `/api/v1` contract plus the hosting architecture it requires.

**Architecture to document:**

- Extract a UI-free **`ServerManager`** — owning `ServerDescription` records,
  their `FileServer` instances, runtime state and `descriptors.xml` persistence —
  into a new project referencing both `server` and `fsserver`. Proposed `admin/`
  (`SimpleDlna.Admin`), consumed by both `sdlna` and `SimpleDLNA`.
- Resolve the `internal` registration surface: make `IHandler` / `IPrefixHandler`
  / `IResponse` / `RegisterHandler` public, vs. `InternalsVisibleTo`.
  **Recommend public** — the admin listener is a legitimate second consumer.
- Add `Guid Id` to `ServerDescription`, generated on load when absent so existing
  `descriptors.xml` files keep working.
- The admin listener: a loopback `TcpListener` on 19199, reusing the existing
  `HttpClient` parse/response machinery, with its own prefix table.

**Endpoint surface** — to be specified in full, with request/response schemas,
status codes and error shape:

```
GET    /api/v1/status                 version, media port, admin port, playback, uptime
GET    /api/v1/capabilities           views[] + orders[] (name/description from the
                                      repositories), media types, restriction types
GET    /api/v1/servers                descriptions + runtime state
POST   /api/v1/servers                create
GET    /api/v1/servers/{id}
PUT    /api/v1/servers/{id}           update; restarts if running (UpdateInfo semantics)
DELETE /api/v1/servers/{id}
POST   /api/v1/servers/{id}/start
POST   /api/v1/servers/{id}/stop
POST   /api/v1/servers/{id}/rescan
POST   /api/v1/servers/rescan-all
GET    /api/v1/settings               port, cacheDir, logLevel, rescanDelay,
PUT    /api/v1/settings               rescanInterval, startMinimized, preventSleep,
                                      autostart (registry-backed)
POST   /api/v1/cache/drop
GET    /api/v1/log?tail=N             replaces "Open Log Folder"
GET    /api/v1/fs?path=               directory listing — replaces FolderBrowserDialog
GET    /api/v1/events                 live state (see below)
```

**Each stack constraint from §3 gets an explicit answer**, in particular:

- Query-string parsing and URL decoding are the API handler's job.
- `HttpCode` + `HttpPhrases` extended with the codes the API actually returns.
- **Live state:** recommend plain polling for v1 (`/events` as a cheap state
  snapshot), and note SSE as a follow-up requiring the response path to support
  open-ended streams. Decide and record.
- `System.Text.Json` with source-generated contexts noted as an option; publish is
  non-trimmed, so reflection is acceptable either way.
- Settings that must move out of user-scoped `user.config` (a tray shell has it, a
  headless console does not) into a JSON config in `CacheDir`, plus the migration
  path from existing `user.config` values.
- Which settings are hot-appliable and which require a restart (port and cache dir
  are restart-only today — the listener is `readonly`).
- Auth: loopback-only binding is the whole model for v1; a token/CSRF story is
  documented as a prerequisite if the bind address ever widens.

---

### Step 3 — SPA design → `modernization.md` §3

- **Layout:** `web/` at the repo root — `package.json`, `vite.config.ts`, `src/`
  — building to `web/dist`, which becomes the embedded `wwwroot`.
- **Screens**, mapped 1:1 onto the §1 inventory: **Servers** (list with live state
  badges; start/stop/rescan/edit/remove/new), **Server editor** (name, order +
  descending, types, views, restrictions, directories, using the `/api/v1/fs`
  picker), **Settings**, **Log viewer** (new — replaces "Open Log Folder"),
  **About**.
- **Parity checklist table:** every §1 control → the SPA element that replaces it,
  or an explicit *"dropped, because…"* (minimize-to-tray, single-instance,
  FolderBrowserDialog, etc.).
- **Deliberate improvements**, called out as new rather than smuggled in:
  parameterised views now that a form can express them; view reordering, which the
  GUI advertised but never implemented; live playback status; a real log viewer.
- Typed API client mirroring the §2 schemas; polling cadence for live state;
  optimistic vs. server-confirmed state transitions.
- Dev loop: Vite dev server proxying `/api/v1` to `localhost:19199`.
- Dark/light and accessibility notes. `browse.css` and the DLNA browse UI stay
  untouched — this SPA is the **admin** surface only.

---

### Step 4 — WinForms deprecation + build integration → `modernization.md` §4

- **Tray shell** — what survives in `SimpleDLNA/`: `Program.cs` (global mutex
  `simpledlnaguilock`, named pipe `simpledlnagui`), `NotifyIcon` with a menu of
  *Open UI* / *Rescan all* / *Exit*, `StartUpUtilities` (autostart), the
  `SleepInhibitor` wiring. What is deleted: `FormMain`, `FormServer`,
  `FormSettings`, `FormAbout`, `ServerListViewItem`, and an assessment of whether
  `NMaier.Windows.Forms/` can be dropped entirely. *Open UI* =
  `Shell("http://localhost:19199/")` via the existing
  `ProcessStartInfo { UseShellExecute = true }` helper.
- **Console parity** — `sdlna.exe` hosts the same admin listener so the API/UI
  works headless; decide whether it is on by default or behind
  `--admin` / `--no-admin` / `--admin-port`.
- **Build wiring** — no custom MSBuild targets exist today. Specify a
  `Target BeforeTargets="BeforeBuild"` running `npm ci && npm run build` with
  `Inputs`/`Outputs` for incrementality, plus a `SkipWebBuild` property so a
  Node-less machine can still build. Add a `web:` target to the `Makefile` as a
  prerequisite of `console` and `gui`; add `actions/setup-node` + `npm ci` to
  `.github/workflows/build-release.yml` before the restore step; add node entries
  to `.gitignore`.
- **Serving the bundle** — `Assembly.GetManifestResourceStream` over the
  `wwwroot\**` glob; manifest-name mangling rules (dots and dashes in Vite's
  hashed filenames), the content-type table, SPA fallback to `index.html`, and
  long-lived `Cache-Control` for hashed assets against today's blanket
  `no-cache`.
- **Rollout** — one release with both front ends behind a flag vs. a clean cut;
  what happens to `user.config` and `descriptors.xml` on upgrade; updates to
  `Readme.md`, `CLAUDE.md`, `SimpleDLNA/CLAUDE.md` and `TODO.md`.

---

## 5. Verification

Per step, before committing:

1. `modernization.md` renders correctly, and every `file:line` reference in the
   new section resolves — spot-check ~10 against the working tree.
2. §1 is mechanically checkable: grep the `*.Designer.cs` files for `.Text = "`
   and reconcile against the inventory so no control is missing.
3. §2 and §3 stay consistent with §1 — every §1 feature appears in the §3 parity
   table as *migrated* or *explicitly dropped*.
4. `git status` shows **only** the one intended file modified
   (`MIGRATION-PLAN.md` for Step 0, `modernization.md` for Steps 1–4), and
   `git show --stat HEAD` after each commit confirms a single-file change.
5. Nothing is built or run in this phase; the source tree is untouched, so
   `dotnet build sdlna.sln` stays green by construction.

**Working-tree note:** there are pre-existing uncommitted changes in `Readme.md`,
`SimpleDLNA/FormMain.cs`, `SimpleDLNA/FormMain.Designer.cs`, `TODO.md`,
`fsserver/FileStore.cs`, `fsserver/Files/Cover.cs`, `fsserver/ItemSerializer.cs`,
`fsserver/CLAUDE.md`, plus an untracked `subs.md`. Every commit in this plan
stages the one intended file only and leaves those alone.

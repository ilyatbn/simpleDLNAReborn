# SimpleDLNA — WinForms tray GUI

Manages a set of named server configurations, each of which mounts a
`FileServer` into one shared `HttpServer`. Persists them to `descriptors.xml`
and runs minimized to the tray.

## Shortcuts

| Need | File |
| --- | --- |
| Main window, tray icon, log pane, server lifecycle | `FormMain.cs` |
| Add/edit a server configuration dialog | `FormServer.cs` |
| Global preferences dialog | `FormSettings.cs` |
| About box | `FormAbout.cs` |
| The persisted per-server config model | `ServerDescription.cs` |
| List row + its state/rendering | `ServerListViewItem.cs` |
| User settings (start minimized, autostart, ...) | `Settings.cs`, `Properties/Settings.Designer.cs` |
| Run-at-login registry handling | `StartUpUtilities.cs` |
| Icons/images | `Properties/Resources.resx` → `Resources/` |

`*.Designer.cs` files are generated layout — edit the form in a designer or
carefully by hand, but keep changes out of the way of regeneration.

## Gotchas

- `FormMain` implements log4net's `IAppender`: log events are queued and flushed
  onto the UI thread on a timer. Anything touching controls from a server thread
  must go through `BeginInvoke` — see `ServerListViewItem.BeginInvoke`.
- Config and logs live under `CacheDir` (per-user app data), not next to the
  exe. `descriptors.xml` there is the source of truth for configured servers.
- `PathEnvironmentInstaller.cs` was deleted in the .NET 10 move: it derived from
  `System.Configuration.Install.Installer`, which does not exist outside .NET
  Framework, and its only caller was the MSI (`setup/setup.vdproj`) that can no
  longer be built. If a real installer comes back, the "add install dir to user
  PATH" behaviour needs reimplementing — see git history for the original.
- Same stale-`#if DEBUG` trap as `sdlna/`: the debug-only log file path had an
  uncompilable identifier for years. If you edit inside `#if DEBUG`, build Debug.

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
| Library refresh timers | `FormSettings.Designer.cs` group `groupBoxRefresh`; applied in `ServerListViewItem.StartFileServer` |
| Logging setup, level list | `FormMain.SetupLogging`, `FormMain.LogLevels` |
| Playback indicator + sleep toggle | `FormMain.UpdatePlaybackState` — the single place playback state turns into behaviour; add new consumers there |
| Run-at-login registry handling | `StartUpUtilities.cs` |
| Icons/images | `Properties/Resources.resx` → `Resources/` |

`*.Designer.cs` files are generated layout — edit the form in a designer or
carefully by hand, but keep changes out of the way of regeneration.

## Logging

There is no log view in the window and no on/off switch — logging always goes to
`sdlna.log` in `CacheDir`, and the settings dialog only chooses the level
(`None`…`Debug`, default `Error`). `FormMain` used to implement log4net's
`IAppender` and pump events into a `ListView`; that is all gone.

`SetupLogging` runs again every time the settings dialog closes, so it calls
`hierarchy.ResetConfiguration()` first. Without that each visit stacks another
appender on the root logger and every line gets written N times.

The appender rolls composite — by date so yesterday is a separate file, and by
size (5MB) so one noisy day cannot fill the disk. `MaxSizeRollBackups = 1` keeps
a single rolled file, which bounds the total at roughly 10MB.

## Gotchas

- Anything touching controls from a server thread must go through `BeginInvoke`
  — see `ServerListViewItem.BeginInvoke`.
- Config and logs live under `CacheDir` (per-user app data), not next to the
  exe. `descriptors.xml` there is the source of truth for configured servers.
- `PathEnvironmentInstaller.cs` was deleted in the .NET 10 move: it derived from
  `System.Configuration.Install.Installer`, which does not exist outside .NET
  Framework, and its only caller was the MSI (`setup/setup.vdproj`) that can no
  longer be built. If a real installer comes back, the "add install dir to user
  PATH" behaviour needs reimplementing — see git history for the original.
- Same stale-`#if DEBUG` trap as `sdlna/`: the debug-only log file path had an
  uncompilable identifier for years. If you edit inside `#if DEBUG`, build Debug.

# SimpleDLNA — tray app

A tray icon that runs the DLNA server and opens the web interface. There are no
windows: `TrayContext` is an `ApplicationContext`, not a `Form`.

Everything a user can configure lives in the web UI (`web/`) over the REST API
(`admin/`). This project only owns the things a browser cannot do: living in the
tray, starting with Windows, and keeping the machine awake.

## Shortcuts

| Need | File |
| --- | --- |
| Tray icon, menu, lifecycle, logging setup | `TrayContext.cs` |
| Single instance, second-launch handoff | `Program.cs` |
| Run-at-login registry handling | `StartUpUtilities.cs` |
| One-time import of the old user.config | `TrayContext.cs` → `LegacySettings` |
| Icons | `Properties/Resources.resx` → `Resources/` |

## What is not here any more

`FormMain`, `FormServer`, `FormSettings`, `FormAbout` and `ServerListViewItem`
were deleted along with the whole `NMaier.Windows.Forms` project. Their server
lifecycle logic became `admin/ServerManager.cs` and `admin/ManagedServer.cs`;
their UI became `web/`. `modernization.md` §1 is a full inventory of what they
did, kept precisely so the parity claim can be checked.

`Properties/Settings.*` and `Settings.cs` survive **only** so the first run of
this build can copy the old user-scoped settings into `settings.json`. Nothing
reads them afterwards, and they can go once nobody upgrades from a pre-web
build.

## Gotchas

- The tray icon is the process. Closing the browser tab does nothing; *Exit*
  in the tray menu is what stops the servers.
- A second launch does not focus a window - there is none. It writes to the
  `simpledlnagui` pipe and the running instance opens the browser.
- `NotifyIcon.Text` throws above 63 characters, hence `Truncate`.
- `Process.Start` needs `UseShellExecute = true` on .NET to open a URL at all.
  The old code had one call site that forgot, and the Homepage menu item was
  broken for years because of it.
- Config and logs live under `%LOCALAPPDATA%\SimpleDLNA`. Configuration stays
  there even when the cache directory setting points elsewhere — see
  `admin/Paths.cs` for why.

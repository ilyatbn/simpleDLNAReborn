2026.08.11
---
- added a per-server Restart, in the web UI and as
  POST /api/v1/servers/{id}/restart. It is also how changed refresh settings
  are applied without restarting the whole application.
- replaced the WinForms GUI with a web interface on http://localhost:19199/,
  bound to loopback only.
- new REST API at /api/v1 covering everything the old GUI could do.
- SimpleDLNA.exe is now a tray icon that opens the web interface; the four
  dialogs and the NMaier.Windows.Forms project are gone.
- sdlna.exe serves the same interface, and gains --managed, --admin-port and
  --no-admin.
- views can now take parameters (e.g. large:size=700) and be reordered, neither
  of which the old dialog could do.
- server start failures are shown instead of silently logged.
- global settings moved from user.config to settings.json, and configuration no
  longer lives under the configurable cache directory - changing that setting
  used to lose every configured server.
- the console now honours "prevent sleep while playing" too.

2026.08.10
---
- updated to .net 10.
- build fully portable (no .net install required)
- properly implemented a file changes lisener.
- removed logging from GUI and put it into files.
- added option to prevent sleep while playing a video.
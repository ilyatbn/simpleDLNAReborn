SimplerDLNA Reborn
===
SimpleDLNA, better, faster, stronger. more modern.

A zero-config DLNA/UPnP-AV media server for Windows, managed from a web
interface.

Main changes:
- upgraded to .net framework 10
- better performance.
- fully portable.
- implemented proper refreshing mechanisms for changes in folders.
- Full REST API + Web management interface.
- other bug fixes and ui changes.


Managing it
---
Both builds serve the same admin interface on **http://localhost:19199/**,
bound to the loopback interface only — it is never reachable from the network,
even though the media itself is.

- `SimpleDLNA.exe` sits in the tray. Double-click the icon, or pick
  *Open SimpleDLNA*, to open the interface.
- `sdlna.exe` prints the URL on startup. Run it with `--managed` to configure
  servers from the web interface instead of the command line; without it,
  servers come from the command line and the interface is read-only apart from
  start/stop/rescan.

Useful flags: `--admin-port=N` to move the interface, `--no-admin` to turn it
off.

Building
---
Needs the .NET 10 SDK and Node.js.

```
make                 # both apps -> dist/console, dist/gui
make web             # rebuild just the web UI
make SKIP_WEB=true   # build without Node; API only, no web UI
```

Video thumbnails additionally require `ffmpeg` on `PATH`.

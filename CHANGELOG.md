2026.09.20
---
- UPnP device identities are derived, not random. Every restart - and every
  re-announce after a network change - used to mint a fresh uuid, so the TV
  added another copy of the server to its list and the old one sat there until
  its max-age expired. Restarting now refreshes the existing entry.
- fixed byte-range handling, which streamed to the end of the file no matter
  what was asked for. A "Range: bytes=1000-50999" answered Content-Length 50000
  and then sent 200 MB. Seeking a TV sends a couple of these a second.
- "bytes=0-0" and any other single-byte range returned the whole file; a probe
  at the very end of a file answered 200 OK with the full Content-Length and a
  sentence of error HTML, so the client waited for a file that never came.
- suffix ranges ("bytes=-500", the last 500 bytes) are supported at all now.
  MP4s keep their index at the end and players do ask for it.
- 416 answers carry a real status, body and Content-Range.
- HTTP keep-alive works. Persistent by default on HTTP/1.1, as the spec says,
  instead of demanding an explicit header while advertising keep-alive anyway -
  which cost a TCP handshake per range request.
- a request split across two TCP packets is parsed correctly instead of
  throwing, and a closed connection is no longer replayed as another copy of
  the previous request.
- responses are pumped double-buffered: the next block is read while the
  current one is written. 550 -> 1290 MB/s over loopback.
- disabled Nagle on media connections.
- file handles in the stream cache actually expire now; the check compared the
  age backwards, so nothing ever did.
- error responses are built per request. Four of them were shared singletons
  that every connection wrote its own headers onto.

2026.09.19
---
- servers now survive a network change. Switching Wi-Fi network, docking, or
  picking up a DHCP lease in another subnet used to leave every mount
  advertising an address that no longer existed: SSDP notifications failed to
  bind with WSAEADDRNOTAVAIL and the server vanished from every player until it
  was restarted by hand.
- the change is detected from the IP addresses and their gateways, from the
  Wi-Fi SSID, or from both (the default) - Settings > Network changes.
- the response is configurable: re-announce the mounts on the new addresses,
  which costs no library rescan and is the default, or restart the servers
  outright.
- new tray menu item "Restart servers", and POST /api/v1/servers/restart-all
  and POST /api/v1/servers/readvertise behind it.
- a datagram queued for an address the machine has left is now one debug line
  instead of a stack trace at ERROR.

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
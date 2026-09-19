0 - fix current bugs
- ~~move away from the c# webforms~~ done
    - ~~webserver~~
    - ~~rest api for everything in the gui~~
    - ~~modern web ui.~~

- ~~"restart server" button~~ done - per-server Restart in the web UI and
  POST /api/v1/servers/{id}/restart
- ~~reload server on network change (vpn)~~ done - IP/gateway/SSID detection,
  re-announce or restart, Settings > Network changes

next up
- optional ffmpeg-backed time seeking (DLNA.ORG_OP=11) - design and
  measurements in timeseek.md; measure on the real TV before building it
- fix the ASCII-lossy request body in server/Http/HttpClient.cs (the
  Encoding.ASCII round-trip around line 318) - it corrupts non-ASCII SOAP
  requests today (modernization.md 2.13 #1)
- describe view parameters from IView instead of the static table in
  admin/Api/ViewParameters.cs (modernization.md 2.5)
- auth if the admin interface ever binds beyond loopback (modernization.md 2.11)

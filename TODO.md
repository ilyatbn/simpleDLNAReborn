0 - fix current bugs
- ~~move away from the c# webforms~~ done
    - ~~webserver~~
    - ~~rest api for everything in the gui~~
    - ~~modern web ui.~~

- ~~"restart server" button~~ done - per-server Restart in the web UI and
  POST /api/v1/servers/{id}/restart
- reload server on network change (vpn)

next up
- fix the ASCII-lossy request body in server/Http/HttpClient.cs:259 - it
  corrupts non-ASCII SOAP requests today (modernization.md 2.13 #1)
- describe view parameters from IView instead of the static table in
  admin/Api/ViewParameters.cs (modernization.md 2.5)
- auth if the admin interface ever binds beyond loopback (modernization.md 2.11)

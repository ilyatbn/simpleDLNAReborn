# web — admin SPA

React 19 + TypeScript + Vite. Built to `web/dist` and embedded into
`SimpleDlna.Admin` as `wwwroot/**`; served from `http://localhost:19199/`.

This is the **admin** surface. The DLNA browse UI that clients and curious
users see is a different thing entirely — server-rendered XML in
`server/Handlers/MediaMount_HTML.cs` with `server/Resources/browse.css` — and
is not touched from here.

## Layout

| Path | Contents |
| --- | --- |
| `src/api/` | Typed client, hand-mirrored from `admin/Api/Dtos.cs` |
| `src/api/live.tsx` | Shared status + servers, SSE subscription, polling fallback |
| `src/features/` | One folder per screen |
| `src/components/` | Modal, toasts, badges, directory picker |
| `src/styles/` | Design tokens and one stylesheet |
| `tools/screenshot.mjs` | Screenshot every screen over CDP |

## Dev loop

```
npm install
npm run dev        # :5173, proxies /api to 127.0.0.1:19199
```

Run `sdlna.exe` or the tray app alongside. `changeOrigin` is deliberately false
in `vite.config.ts` so the `Origin` header stays a loopback host and the API's
origin check accepts it.

## Deliberately small

Three runtime dependencies: react, react-dom, react-router-dom. No component
library and no data-fetching library — five screens and six resources do not
justify either, and the bundle is embedded in the assembly, so size is a real
constraint. Budget is 250 KB gzipped; it currently sits around 82 KB.

`src/api/useAsync.ts` is the whole caching story. Invalidation is explicit and
driven by SSE, so there is nothing for a cache library to be clever about.

## Gotchas

- The app must stay usable with SSE dead: `live.tsx` falls back to polling and
  keeps retrying the connection. Never make a screen depend on an event
  arriving.
- Events are nudges, not state. Treat every one as "refetch", never as data.
- The backend can exit while the page stays open. That is what the offline
  banner is for; do not remove it because "the fetch will just fail".
- `status.managed` is false when the console was started with directory
  arguments. Mutating UI must hide itself, because the API will answer 409.
- Assets must land under `/assets/` — `Http/WebAssets.cs` keys its
  one-year immutable caching off that prefix.

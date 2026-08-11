# External subtitle support — research notes and implementation plan

> Status: **shelved, not started.** No code has been changed. This file captures
> everything learned while investigating, so the work can be picked up cold.
>
> Goal when resumed: dropping `movie.srt` next to `movie.mkv` should make
> subtitles available on an **LG webOS TV**.
>
> Decisions already taken: read `.srt` **directly with no ffmpeg dependency**;
> use ffmpeg only as a fallback for `.ass`/`.ssa`/`.sub`. Testing to be done on
> the TV by the user.

---

## TL;DR

External subtitles are **already ~70% implemented** and broken in three
independent places. DLNA genuinely has no single standard for advertising
external subtitles (that part of the original hunch was right), but ffmpeg is
**not** needed for `.srt` — it is already the exact format TVs want, and the
current code pipes it through ffmpeg pointlessly, which is why nothing works on
a machine without ffmpeg installed.

Effort estimate: **moderate.** Phase 1+2 (the parts that make it work) are a few
hundred lines across five files. Phases 3–4 are polish and robustness.

---

## 1. What already exists and works

| Piece | Location |
| --- | --- |
| Sibling-file discovery (5 formats) | `server/Types/SubTitle.cs:146-183` |
| HTTP route serving the subtitle | `server/Handlers/MediaMount.cs:129-137` |
| Response construction | `server/Responses/ItemResponse.cs` |
| MIME + PN entries | `DlnaMaps.cs:99` (`smi/caption`), `DlnaMaps.cs:178-182` (PN `SRT`) |
| Samsung capability advertisement | `server/Resources/description.xml:21-22` |
| Cache persistence of extracted text | `fsserver/ItemSerializer.cs:41`, `VideoFile.cs:69,222` |
| Eager warm-up at scan time | `fsserver/BackgroundCacher.cs:48-49` |

`Subtitle` is `[Serializable] public sealed class Subtitle : IMediaResource,
ISerializable`. Text lives in a `string text`; `CreateContentStream()` does
`Encoding.UTF8.GetBytes(text)` (no BOM). `Type => DlnaMime.SubtitleSRT`.

---

## 2. What is broken

### 2a. Everything routes through ffmpeg
`SubTitle.cs:160` calls `FFmpeg.GetSubtitleSubrip(sti)` even for a plain `.srt`
that is already in the target format. `util/Ffmpeg.cs:240-296`:
- args: `-i "<path>" -map s:0 -f srt pipe:`
- **every** failure becomes `NotSupportedException` — missing binary, no
  subtitle track, crash, timeout. Callers cannot tell them apart.
- `FFmpegExecutable` is `null` when ffmpeg is not on PATH → feature entirely dead.
- Output is post-processed: `line.Trim()` and a `^,+` regex strip (`Ffmpeg.cs:31`),
  and lines are joined with bare `\n` (not CRLF).

### 2b. `Subtitle.Load` bugs (`SubTitle.cs:146-183`)
- **(a) Unconditional clobber.** The post-loop `FFmpeg.GetSubtitleSubrip(file)`
  on the *video* at lines 169-178 is not guarded by `HasSubtitle`, so an
  embedded track silently overwrites an external `.srt`. Only "works" today
  because it throws before assigning when there is no embedded track.
- **(b) No `break`.** The last matching extension wins, not the first — `.vtt`
  beats `.srt` because it is last in the array.
- **(c) Uppercase duplicates are harmful.** `exts` lists `.srt`/`.SRT` etc.;
  `FileInfo.Exists` is case-insensitive on NTFS, so ffmpeg is spawned **twice**
  per file (each with a 20 s budget).
- **(d) Only two name shapes probed** — `Path.ChangeExtension` (`movie.srt`) and
  `path + ext` (`movie.mkv.srt`). The very common `movie.en.srt` /
  `movie.eng.srt` is never found. No `Subs/` subdirectory.
- **(e) `catch (NotSupportedException) {}` at 163-164 is completely silent** — the
  most common failure produces zero diagnostics.

### 2c. Nothing is advertised to non-Samsung clients
The **only** advertisement is `ItemResponse.cs:43-51`, which adds a
`CaptionInfo.sec` response header *only if* the client sent the Samsung
`getCaptionInfo.sec` request header. LG does not send it.

Dead code at `MediaMount_SOAP.cs:190-211` under `#if ANNOUNCE_SUBTITLE_IN_SOAP`
(never defined anywhere) would emit `sec:CaptionInfoEx`. It is Samsung-only and
**would not compile**: it calls `mvi.SubTitle` (property is `Subtitle`) and
references `prefix`, which is not in scope because `AddVideoProperties` is
`static` while `Prefix` is an instance property. Original author's comment says
it was skipped because forcing subtitle extraction at browse time was costly —
which was true when every read spawned ffmpeg.

So the DIDL-Lite browse result the TV receives contains **no subtitle
information at all**.

### 2d. Subtitles added after the scan can never appear
`VideoFile.Subtitle` (`VideoFile.cs:180-195`) memoises the result **including
the negative**, and persists it via `info.AddValue("st", subTitle)`
(`VideoFile.cs:222`). The `FileStore` row is keyed on the **video's**
path+size+mtime, which do not change when an `.srt` lands beside it. Restarting
does not help — the negative rehydrates from SQLite. Only a cache wipe fixes it.

### 2e. Route has no `HasSubtitle` guard
`MediaMount.cs:129-137` serves `subtitle/{id}/...` without checking. For a video
with no subtitle, `Body` throws `NotSupportedException` mid-send.

---

## 3. Key constraints discovered (do not violate)

- **Do NOT add subtitle extensions to `ExtensionFilter` / `Media2Ext` /
  `Ext2Media`.** `.srt` would become a browsable media item and
  `FileServer.GetFile` would throw `KeyNotFoundException` at
  `DlnaMaps.Ext2Dlna[ext]`. Subtitles are correctly excluded today.
- **`soapCache` key is `Prefix + sparams.HeaderBlock`** (`MediaMount_SOAP.cs:346`)
  — it contains **neither User-Agent nor LocalEndPoint**. Therefore:
  - Unconditional (client-agnostic) DIDL additions need no cache change.
  - Any per-client branching **requires** extending the key, or the first
    client's DIDL is served to everyone.
  - Pre-existing bug: absolute `http://addr:port/...` URLs are already baked
    into the cache, so on a multi-homed host the first browser pins the address
    for everyone. Not introduced by this work, but inherited.
- **`soapCache` is invalidated only** by `MediaMount.ChangedServer` (any rescan)
  and `HandleXSetBookmark`. Both are full `Clear()`.
- **Any change to the `Subtitle` serialized payload requires bumping
  `FileStore.SCHEMA`** (`fsserver/FileStore.cs:17`, currently `0x20260810`).
- **`DlnaMaps.Mime[DlnaMime.SubtitleSRT]` has exactly one consumer** —
  `ItemResponse.cs:28`, and only when the item is a `Subtitle`. Changing it is
  a one-header blast radius.
- `BrowseMetadata` on an **item** id throws `ArgumentException("Invalid id")`
  → SOAP fault (`MediaMount_SOAP.cs:371-374`); only folders are handled. If a
  TV does `BrowseMetadata` before playing, that path fails today.

### Corrections to assumptions made during planning
Four things that look like problems but are not:

1. **Missing `Content-Length` on subtitles is not real.**
   `HttpClient.GetContentLengthFromStream` (`HttpClient.cs:138-155`) backfills it
   from the stream length whenever the header is absent/unparseable. Implementing
   `IMetaInfo` on `Subtitle` is cosmetic + `Last-Modified`, not a fix.
2. **`Subtitle.MediaType`'s `NotImplementedException` is unreachable.**
   `ItemResponse.IsPlayback` short-circuits on `transferMode == "Streaming"`,
   and subtitles use `"Background"`. Latent landmine, not a live bug.
3. **No second `FileSystemWatcher` is needed.** `FileServer.Load` never sets
   `watcher.Filter`, so the default `*.*` **already delivers `.srt` events** into
   `OnChanged`/`OnRenamed`. They are discarded purely by the
   `Filter.Filtered(ext)` test at `FileServer.cs:249`/`:332`. An early branch
   before that test is all that is required.
4. **`FileStore` does not need a per-row delete.** The insert is
   `INSERT OR REPLACE` on a `PRIMARY KEY ON CONFLICT REPLACE`
   (`FileStore.cs:84-86`), so invalidation is a re-store.

---

## 4. Implementation plan

### Phase 1 — make `.srt` load (all four steps required together)

1. **`server/Types/SubTitle.cs` — rewrite `Load`.**
   - Single lowercase priority table: `.srt`, `.ass`, `.ssa`, `.sub`, `.vtt`
     (`.vtt` last, since it goes via ffmpeg — see note below).
   - Enumerate the directory **once**, OS-filtered:
     `video.Directory.EnumerateFiles(stem + ".*", opts)` with
     `EnumerationOptions { MatchCasing = CaseInsensitive, IgnoreInaccessible = true }`.
     Finds `movie.srt`, `movie.mkv.srt` **and** `movie.en.srt` in one pass.
     (`*` spans `.` in Win32 wildcards; `*`/`?` are illegal in NTFS names so the
     stem needs no escaping.) Skip empty files and anything > ~16 MB.
   - Rank by (format priority, language-tag preference, name); **`break` on first
     success**.
   - Dispatch: `.srt` → direct read; everything else → `FFmpeg.GetSubtitleSubrip`.
   - Embedded-track extraction **only when no external file was found** — the
     single most important structural fix.
   - Replace the silent `catch` with a debug log. Record the source `FileInfo`
     for `InfoDate`.
2. **`util/Encodings.cs` (new) + `util/util.csproj`.** BOM sniff → strict UTF-8
   (`new UTF8Encoding(false, throwOnInvalidBytes: true)` — a strong
   discriminator, legacy prose almost never forms valid UTF-8) → legacy
   fallback. Add the `System.Text.Encoding.CodePages` package and register
   `CodePagesEncodingProvider.Instance` in the **static constructor of
   `Encodings`**, not in either `Program.cs` — both entry points then get it
   with no ordering hazard. Without it only Latin1 is available, which
   mojibakes Cyrillic/Hebrew/Polish. `util` is the right layer (bottom of the
   stack, no in-repo deps).
3. **`fsserver/Files/VideoFile.cs` — stop persisting the subtitle.** Remove
   `info.AddValue("st", …)` and the matching `info.GetValue("st", …)`. Replace
   memoisation with a `subTitleProbed` bool so the negative is cached in-process
   but never crosses a process boundary. `Server.UpdateFileCache(this)` in the
   getter goes away with it. **Keep** `{"subtitle", typeof(Subtitle)}` in
   `ItemSerializer.Types` — tags are persisted and must never be reused.
4. **`fsserver/FileStore.cs:17` — bump `SCHEMA`** to `0x20260811`. Required by
   the payload change, and conveniently flushes every stale negative.

> Steps 3–4 are **not optional**: without them every already-scanned video keeps
> its cached "no subtitle" answer and phase 1 looks like a no-op.

### Phase 2 — make the LG see it

5. **`server/Handlers/MediaMount_SOAP.cs`.**
   - Add `NS_PV = "http://www.pv.com/pvns/"`; declare `xmlns:pv` on the root
     DIDL element (~line 381) alongside the existing four. **Required** — without
     the ancestor binding, `XmlDocument` re-emits the declaration on every element.
   - Add a new **instance** method `AddSubtitle(IRequest, IMediaResource, XmlNode)`.
     Do *not* de-static `AddVideoProperties` — smaller diff, and `Prefix` is only
     reachable from the instance. Call it from `Browse_AddItem` after `AddCover`.
     Guard on `HasSubtitle`.
   - Emit all four, all pointing at
     `http://{LocalEndPoint}/mm-N/subtitle/{id}/{name}.srt`:
     - `<res protocolInfo="http-get:*:text/srt:*">` — generic renderers
     - `<sec:CaptionInfoEx sec:type="srt">` and `<sec:CaptionInfo sec:type="srt">`
     - `<pv:subtitleFileUri>` + `<pv:subtitleFileType>srt</…>` — PacketVideo, the
       stack LG licensed; highest-probability hit for webOS
   - **Do not put `DLNA.ORG_PN=SRT` in the protocolInfo.** No such registered
     DLNA profile exists (`DlnaMaps.AllPN` invents it internally); validating
     renderers drop the whole `<res>` — the classic "TV sees nothing" failure.
     Use a bare `*`.
   - Delete the dead `#if ANNOUNCE_SUBTITLE_IN_SOAP` block.
   - The route ignores the trailing path segment (`path.Split('/')[1]`), so a
     `.srt`-suffixed filename is free; several webOS builds key off the URI
     extension.
6. **`server/Types/DlnaMaps.cs:99`** — `"smi/caption"` → `"text/srt"`.
   `smi/caption` is Samsung's convention and LG does not recognise it.

### Phase 3 — correctness

7. `MediaMount.cs:129-137` — guard the route on `HasSubtitle`, return 404.
8. `SubTitle.cs` — implement `IMetaInfo` (members already exist); make
   `InfoDate` return the source file's `LastWriteTimeUtc` instead of
   `DateTime.UtcNow` (which defeats client caching); implement `MediaType`.
9. `ItemResponse.cs` — suppress `contentFeatures.dlna.org` for `Background`
   transfers, so step 8 does not start advertising the bogus `DLNA.ORG_PN=SRT`.

### Phase 4 — pick up subtitles added later

10. `fsserver/FileServer.cs` — early branch in `OnChanged`/`OnRenamed` **before**
    the `Filter.Filtered(ext)` test. Find sibling `VideoFile`s in the same
    `PlainFolder`, call `InvalidateSubtitle()`, raise `Changed` (which clears
    `soapCache`, bumps `systemID` and fires the `ContainerUpdateIDs` NOTIFY, so
    the TV re-browses). Return `true` so a subtitle drop does not trigger a full
    tree rescan.
11. `fsserver/Files/VideoFile.cs` — `internal void InvalidateSubtitle()`.
12. `SubTitle.cs` — public static `IsSubtitleExtension(string)` so `fsserver`
    does not duplicate the table.

### Optional polish
- UTF-8 BOM in `CreateContentStream` — known webOS mojibake fix; promote to
  phase 2 if the TV shows garbage. A minority of players instead render it as a
  stray glyph on cue 1.
- Codepage-scoring fallback (score by C1 control chars and script consistency)
  instead of a bare cp1252 guess.
- **VTT→SRT normaliser** (~40 lines: strip `WEBVTT`/`NOTE`/`STYLE`, drop cue
  identifiers and settings, `.` → `,` in timestamps, renumber). Until then keep
  `.vtt` last and route it through ffmpeg — reading it directly and serving it
  as `text/srt` renders as garbage.
- Per-directory LRU of the enumeration result keyed on
  `dir.FullName + LastWriteTimeUtc` — only if a cold browse of a large folder
  measurably drags. Measure first.
- `--subtitle-language` / `--subtitle-encoding` CLI options.

---

## 5. Verification

Server-side, no TV needed:

1. `dotnet build sdlna.sln`
2. Scratch folder with a video + hand-written `movie.srt`;
   `sdlna.exe -p 18080 --log-level=DEBUG <folder>`
3. Log shows `Loaded subtitle from …movie.srt` with **no** ffmpeg lines.
4. `curl -i http://localhost:18080/mm-1/subtitle/<id>/st.srt` → 200,
   `Content-Type: text/srt`, correct `Content-Length`, exact text.
   (Get ids from `http://localhost:18080/mm-1/index/0`.)
5. POST a `Browse` to `/mm-1/control`; confirm all four elements and `xmlns:pv`
   on the root.
6. Negative case: subtitle URL of a video with no `.srt` → 404, not 500.
7. Phase 4: copy an `.srt` in while running; confirm the branch fires and a
   re-issued Browse now carries the elements.
8. Save an `.srt` as windows-1251; confirm served bytes are valid UTF-8.

Then on the TV. If it still ignores the subtitle, try **one at a time**:
1. `application/x-subrip` instead of `text/srt` (header + protocolInfo)
2. add a second `<res>` with `http-get:*:smi/caption:*`
3. prepend a UTF-8 BOM
4. make the subtitle URL basename byte-identical to the video filename

---

## 6. Risks

- **Medium — phase 2 enlarges the DIDL for every client on every browse.** Watch
  for an old renderer choking on the unknown `pv:` prefix and dropping the whole
  item. If a previously-working client shows an empty folder, remove `pv:` first.
- **Medium — the MIME change trades Samsung compatibility for LG.** Samsung
  follows the `CaptionInfo.sec` URL and mostly ignores content type, so risk is
  low but real, and it is a device class that cannot be tested here.
- **Low — phases 1, 3, 4** touch code that is currently dead or broken.
- **Unverifiable locally:** which advertisement mechanism webOS actually
  honours. That is exactly why all of them ship at once, unconditionally.
- `.sub` is ambiguous — MicroDVD text (ffmpeg guesses 23.976 fps) vs binary
  VobSub (needs a paired `.idx` + OCR, `-f srt` cannot do it). Expect `.sub` to
  fail often; do not advertise it as supported.

---

## 7. Docs to update when implemented

- `server/CLAUDE.md` — the `SubTitle.cs` gotcha becomes wrong once persistence
  is dropped; note the `smi/caption` → `text/srt` change.
- `fsserver/CLAUDE.md` — the change-detection section should mention the
  subtitle branch.

---

## 8. Incidental findings (unrelated bugs spotted en route)

Not part of this work, recorded so they are not lost:

- `Ffmpeg.cs:135-144` — `infoCache` is read but **never written**, so the LRU is
  dead and every `IdentifyFile` call re-spawns ffmpeg.
- `MediaMount_SOAP.cs:83-95` — `AddCover` sets `protocolInfo` twice; the first
  `SetAttribute` is dead code.
- `MediaMount_SOAP.cs:247` — `Browse_AddItem` hardcodes
  `parentID = Identifiers.GENERAL_ROOT` rather than the real parent.
- `MediaMount_SOAP.cs:355` — a failed `int.TryParse` of `RequestedCount` leaves
  `requested = 0`, meaning "return everything".
- `util/GlobalSuppressions.cs:7-9` — still references `GetSubtitleSRT`, renamed
  to `GetSubtitleSubrip` long ago.
- `Subtitle.Path` is the constant `"ad-hoc-subtitle:"` and `Id` returns it, so
  every `Subtitle` shares one Id. Harmless today — it is only ever reached via
  the owning video's id — but surprising.

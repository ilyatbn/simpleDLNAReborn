# Milestone: ffmpeg-backed time seeking

Status: **not started.** Design notes and measurements from 2026-09-19, written
down so the next person does not have to re-derive them.

## What is missing today

Every resource is advertised `DLNA.ORG_OP=01` — byte-seek yes, time-seek no —
and `DlnaFlags.TimeBasedSeek` is left off. That is honest: nothing in the server
implements `TimeSeekRange.dlna.org`, and claiming it would be worse than not
claiming it.

Advertised in three places, all of which have to agree:

| Where | What it builds |
| --- | --- |
| `server/Handlers/MediaMount_SOAP.cs` (~84, ~277) | `protocolInfo` on each `res` element of a Browse/Search result |
| `server/Responses/ItemResponse.cs` (~34) | the `contentFeatures.dlna.org` response header |
| `server/Types/DlnaMaps.cs` (~359) | the `SourceProtocolInfo` list for ConnectionManager |

`DlnaFlags` already has `TimeBasedSeek` (bit 30) defined and unused.

## Why it might be worth doing

A client that can only byte-seek has to turn "jump to 01:12:30" into a byte
offset itself. With constant bitrate that is arithmetic. With VBR — which is
everything modern — it is a guess, so the player either lands wrong and hunts,
or greys out the scrub bar entirely. Behaviour varies wildly by TV, which is
why this should be measured on real hardware before it is built.

**Measure first.** As of 2026-09-19 the server delivers ~1290 MB/s over
loopback and answers a ranged request in ~0.85 ms regardless of seek depth.
Wi-Fi tops out 20-100× below that. Nothing about seek *latency* is server-side
any more, so the only thing this milestone can buy is seek *accuracy* on
formats where the client cannot map time to bytes. If a given TV already seeks
accurately, this does nothing for it.

## What it involves

### 1. An index per file

Time → byte offset for keyframes. `ffprobe -show_packets -skip_frame nokey`
gives it; on a large file it is slow and I/O heavy, so it cannot happen during
a Browse.

Storage belongs in the existing SQLite cache (`fsserver/FileStore.cs`),
keyed the same way as everything else (path + size + mtime). Read
`fsserver/CLAUDE.md` before touching the serializer: a new payload means a new
`Kind` in `ItemSerializer`, an entry in `ItemSerializer.Types`, **and** a bump
of `FileStore.SCHEMA`, which drops every existing cache.

Sizing: one entry per keyframe, keyframes typically 2-10s apart, so a 2 hour
film is ~1-4k entries. A packed `long[]` of byte offsets plus a fixed time base
is a few tens of KB — fine to store, too big to want to rebuild often.

### 2. Building it without stalling anything

`fsserver/BackgroundCacher.cs` is the existing precedent for "walk the library
slowly in the background". Indexing should ride along there, not in
`FileServer.Load`, and it must be interruptible — a rescan or a shutdown should
not wait on it.

Open question worth settling early: index every video on sight, or only on the
first seek of a file? Lazy is cheaper and self-targeting, but the first seek of
a film then pays for the whole index, which is exactly the moment the user is
watching. Probably: lazy trigger, then cache, and let `BackgroundCacher`
pre-warm recently played items.

### 3. Serving it

`TimeSeekRange.dlna.org: npt=1234.5-` on the request. The response carries
both the time range and the byte range it resolved to:

```
TimeSeekRange.dlna.org: npt=1234.5-5678.9/5678.9 bytes=12345678-98765432/98765432
```

This lives next to `ProcessRanges` in `server/Http/HttpClient.cs` and produces
the same thing it does — a seek plus a `LimitedStream`. The range plumbing is
already correct as of 2026-09, so this is a second entry point into it rather
than new machinery. `X_GetFeatureList` in `MediaMount_SOAP.cs:529` may also
need to grow a seek entry.

### 4. Only claim it when it is true

`DLNA.ORG_OP` has to become `11` *per resource*, and only for a file that
actually has an index. A file still being indexed, or one ffmpeg could not
parse, must keep advertising `01`. That means `protocolInfo` stops being a
constant string and starts depending on per-item state — which is the part most
likely to be got wrong, because all three call sites above must agree with each
other and with what the HTTP layer will really honour.

## Make it optional

ffmpeg is already optional (`util/Ffmpeg.cs` probes for it; thumbnails degrade
gracefully without it, see the 2026-09 cover handling). Time-seek indexing has
to degrade the same way: no ffmpeg, no index, `DLNA.ORG_OP=01`, everything
still works. A setting alongside the network-change options in
`admin/AppSettings.cs` — off by default until it has been tested on real
hardware.

## Rough order

1. Confirm on the actual TV that byte-seek is inaccurate. If it is not, stop.
2. `ffprobe` keyframe extraction behind an `Ffmpeg.cs` helper, measured for
   cost on a 4K file.
3. Cache schema + serializer, with the `FileStore.SCHEMA` bump.
4. Lazy index-on-first-seek, then background pre-warm.
5. `TimeSeekRange.dlna.org` request/response handling.
6. Per-resource `DLNA.ORG_OP` and the `TimeBasedSeek` flag.
7. Setting, default off.

Steps 2-4 are the bulk of it. Steps 5-6 are small given the range work already
done, and are worthless without 2-4.

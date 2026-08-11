# fsserver — SimpleDlna.FileMediaServer

Implements the `server/` media interfaces on top of a real directory tree:
scanning, per-file metadata extraction (taglib), cover art, and the SQLite
cache that makes restarts fast.

## Shortcuts

| Need | File |
| --- | --- |
| Mount point, rescan, cache wiring | `FileServer.cs` |
| Directory tree nodes | `PlainFolder.cs`, `PlainRootFolder.cs` |
| Which extensions map to which media type | `ExtensionFilter.cs` |
| Common file behaviour (title, size, cover) | `Files/BaseFile.cs` |
| Per-type metadata via taglib | `Files/AudioFile.cs`, `Files/VideoFile.cs`, `Files/ImageFile.cs` |
| Cover art extraction + thumbnailing | `Files/Cover.cs` |
| SQLite metadata cache | `FileStore.cs` |
| Cache wire format | `ItemSerializer.cs` |
| Background pre-caching of metadata | `BackgroundCacher.cs` |
| Open file handle reuse | `Files/FileStreamCache.cs` |

## The cache — read this before touching metadata

`FileStore.cs` keeps one SQLite row per file, keyed by path + size + mtime, with
two blobs: the serialized item and its serialized cover.

`ItemSerializer.cs` produces those blobs. It replaced `BinaryFormatter`, which
was removed from the runtime in .NET 9. It keeps the `ISerializable` /
`SerializationInfo` contract the file classes already used, so:

- A cacheable type needs `[Serializable]`, a `GetObjectData`, a private
  `(SerializationInfo, StreamingContext)` constructor, **and** an entry in
  `ItemSerializer.Types`. Missing the last one means it silently stops being
  cached (`CanSerialize` returns false).
- Only these value kinds round-trip: null, string, int, long, bool, double,
  `byte[]`, `string[]`, and nested registered objects. Adding a new field type
  means adding a `Kind`.
- **Any change to a `GetObjectData` payload requires bumping `FileStore.SCHEMA`**,
  which drops and rebuilds existing cache files. Skipping this hands stale
  payloads to a constructor that no longer expects them.
- `Serialize` builds the record in a scratch buffer and only then copies it to
  the destination. Writing straight through would leave the magic header in the
  stream when `GetObjectData` throws, and a caller that persisted those bytes
  would create a record that fails to deserialize forever.
- **A cover is only persisted when it actually has bytes** (`Cover.HasData`).
  `Cover.GetObjectData` throws when it holds none — which is the normal case for
  videos on a machine without ffmpeg — and storing the result anyway is what
  produced 138 FATALs' worth of truncated records.
- **A damaged record is a cache miss, never an exception.** Both `MaybeGetFile`
  and `MaybeGetCover` log at Debug and return null; `MaybeGetCover` also calls
  `ClearCover` so the bad row heals itself. That last part is required because
  the INSERT's `COALESCE` deliberately preserves an existing cover blob when the
  new one is null, so re-storing the item cannot clear it.
- The type tag strings in `ItemSerializer.Types` are persisted. Never reuse a
  tag for a different type.

Deserialization gets context through `StreamingContext.Context`, which carries a
`DeserializeInfo` (`DeserializeInfo.cs`) holding the server, `FileInfo` and mime
type — that is how a deserialized item gets reattached to a live file.

## Change detection

`FileServer` watches every mounted directory with a `FileSystemWatcher` and
debounces into a single rescan (`ChangeDelay`, default 5s). `RescanInterval`
(default 30min, `Zero` disables) is only a safety net for changes the watcher
cannot see, such as edits on a network share.

`OnChanged`/`OnRenamed` filter by file extension, and a directory has none, so
directory events must be short-circuited through `LooksLikeDirectory` *before*
the filter. Forgetting that is what made folder add/remove invisible until the
periodic rescan fired — for up to half an hour.

Every rescan raises `Changed`, which is what `MediaMount` turns into a UPnP
NOTIFY. Breaking that event chain silently stops clients from refreshing, and
nothing in the server logs will look wrong.

## Gotchas

- `Files/ImageFile.cs` can log an `InvalidOperationException` out of
  `TagLib.IFD.IFDReader` when several JPEGs are read concurrently by
  `BackgroundCacher`. It is caught and only costs that file its metadata, but it
  is a real thread-safety problem inside TagLibSharp, not our code.
- Serialization only stores metadata, never file content. A cache hit still
  stats the file; a size/mtime change invalidates the row.

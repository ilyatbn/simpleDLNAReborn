# thumbs — SimpleDlna.Thumbnails

Turns a media file into a small JPEG for the browse UI and for DLNA clients.

## Shortcuts

| Need | File |
| --- | --- |
| Entry point + in-memory LRU cache + loader dispatch | `ThumbnailMaker.cs` |
| Resize/letterbox maths | `ThumbnailMaker.cs` (`ResizeImage`), `ThumbnailMakerBorder.cs` |
| Still images | `ImageThumbnailLoader.cs` |
| Video frames (shells out to ffmpeg) | `VideoThumbnailLoader.cs` |
| Loader contract | `IThumbnailLoader.cs` |

## How dispatch works

`ThumbnailMaker`'s static constructor reflects over this assembly, instantiates
every `IThumbnailLoader` with a parameterless constructor, and indexes them by
the `DlnaMediaTypes` each one advertises via `Handling`. So adding a loader is
just adding a class — no registration list.

A loader signals "I am not usable here" by **throwing from its constructor**;
`VideoThumbnailLoader` does exactly that when ffmpeg is not on `PATH`. That
throw is caught per loader and logged. It used to escape and take the whole
static constructor with it, which disabled *all* thumbnails — including image
ones — on any machine without ffmpeg. Keep the try/catch.

## Gotchas

- This is the main `System.Drawing.Common` consumer, and `System.Drawing` is
  Windows-only on modern .NET. This project is why the whole solution targets
  `net10.0-windows`. Cross-platform means porting these three files to
  ImageSharp or SkiaSharp.
- Video thumbnails need `ffmpeg` on `PATH`; `util/Ffmpeg.cs` does the probing.
  Without it, videos simply get no thumbnail.

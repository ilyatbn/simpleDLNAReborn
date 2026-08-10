# sdlna — console entry point

Thin shell: parse options, build a `FileServer`, register it with an
`HttpServer`, block until Ctrl-C. All the actual work is in `server/` and
`fsserver/`.

## Shortcuts

| Need | File |
| --- | --- |
| `Main`, server wiring, shutdown | `Program.cs` |
| Every command line flag, plus log4net setup | `Options.cs` |
| Console window icon (Win32) | `ProgramIcon.cs`, `SafeNativeMethods.cs` |
| Embedded LICENSE shown by `--license` | `Properties/Resources.resx` → `Resources/LICENSE` |

## Adding a command line option

Add a public field or property to `Options.cs` with `[Argument("name", ...)]`
plus optionally `[ShortArgument('x')]` / `[FlagArgument(true)]`, and read it in
`Program.Main`. GetOptNet builds `--help` from those attributes, so there is no
separate usage text to update. Validation goes in a property setter that throws
`GetOptException` — see `Port` / `Ips` / `Macs`.

## Gotchas

- **Long options take `=`**: `--cache=file`, `--log-level=DEBUG`. `--cache file`
  is rejected with "Omitted value for argument". Short forms take a space.
- `Console.TreatControlCAsInput` throws on modern .NET when there is no console
  attached (redirected output, service host, scheduled task), so it is guarded
  by `Console.IsInputRedirected`. Same class of trap applies to other `Console`
  properties if you add any.
- The general `catch (Exception)` around `Main` is compiled out in Debug
  (`#if !DEBUG`), so Debug builds surface crashes and Release builds log them.
  Debug-only code paths here have gone stale before — a bad identifier inside
  `#if DEBUG` sat uncompiled for years.
- Package `GetOptNet` is at 4.0.8; the 1.2 assembly this used to reference was
  .NET 3.5-only and cannot load on .NET 10. The attribute API is unchanged.

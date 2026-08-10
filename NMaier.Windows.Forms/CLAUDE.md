# NMaier.Windows.Forms

Three files of WinForms cosmetics, used only by `SimpleDLNA/`. No dependency on
anything else in the repo, and it has its own strong-name key
(`NMaier.Windows.Forms.snk`) rather than the shared `sdlna.key.snk`.

## Shortcuts

| Need | File |
| --- | --- |
| Base `Form` — system fonts, flat style, double buffering, `BoldFont`/`ItalicFont` | `Form.cs` |
| Toolbar/menu renderer that uses real system visual styles | `ToolStripRealSystemRenderer.cs` |

`SimpleDLNA`'s forms derive from this `Form` via `using Form = NMaier.Windows.Forms.Form;`,
so a change here reskins every dialog.

## Gotchas

- Public properties on a `Form` subclass need
  `[DesignerSerializationVisibility(...)]` or the WinForms source analyzer fails
  the build with **WFO1000** (it is an error, not a warning). `BoldFont` and
  `ItalicFont` are marked `Hidden` because they are derived from the ambient
  `Font` at construction time.
- `using win = System.Windows.Forms;` trips CS8981 (all-lowercase type name may
  become reserved). Harmless today; renaming the alias would touch both files.

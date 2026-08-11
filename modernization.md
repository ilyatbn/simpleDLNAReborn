# simpleDLNA modernization

Working document for the WinForms → web admin UI migration. The process, the
decisions and the step gating live in [`MIGRATION-PLAN.md`](MIGRATION-PLAN.md);
this file holds the four deliverable sections.

| Section | Status |
| --- | --- |
| §1 GUI feature inventory | ✅ done |
| §2 REST API design | ✅ done |
| §3 SPA design | ⬜ not started |
| §4 WinForms deprecation + build integration | ⬜ not started |

---

# §1 — GUI feature inventory

Everything the WinForms GUI exposes, so feature parity can be checked
mechanically rather than from memory. Labels are **verbatim** — quoted exactly as
they appear on screen. Every claim carries a `file:line` reference into
`SimpleDLNA/`.

Read §1.12 first if you only want the checklist; the rest is the evidence.

## 1.1 Application shell and process behaviour

Entry point `Program.cs:11`, `[STAThread] Main()`.

| Behaviour | Where | Detail |
| --- | --- | --- |
| Single instance | `Program.cs:13` | Global named mutex `Global\simpledlnaguilock` |
| Second-instance handoff | `Program.cs:15-26` | If the mutex is held, connect to named pipe `simpledlnagui` (10 s timeout), write one byte, exit. `#if !DEBUG` only |
| Pipe server | `FormMain.cs:639-670` | Background thread, infinite loop on `NamedPipeServerStream("simpledlnagui")`; each received byte un-hides and fronts the window. Skipped under `DEBUG` and on Mono |
| Fatal error handling | `Program.cs:34-46` | Logs `Fatal`, shows a MessageBox, rethrows |
| Window title | `FormMain.cs:468` designer, `:636` runtime | `"SimpleDLNA"` → `"SimpleDLNA - Port {RealPort}"`; `"Going down..."` while closing (`:314`) |
| Tray tooltip | `FormMain.cs:165-172` | The `Text` setter mirrors the title onto `notifyIcon.Text` |
| Start minimized | `FormMain.cs:42`, `:672-682` | `minimized` seeded from `config.startminimized`; `SetVisibleCore` forces the window invisible while set and toggles `notifyIcon.Visible = !value` — **the tray icon exists only while the window is hidden** |
| Minimize to tray | `FormMain.cs:330-337` | On `WindowState == Minimized`: `ShowInTaskbar = false`, `Hide()` |
| Close to tray | `FormMain.cs:322-328` | `FormClosing` is cancelled unless `canClose`; the X button minimizes instead. `canClose` is set only by the two Exit items (`:305-310`) |
| Shutdown | `FormMain.cs:312-320` | Unsubscribe playback, `httpServer.Dispose()`, `sleepInhibitor.Dispose()` |

There are **no balloon tips, toasts or notifications** anywhere in the project.

## 1.2 FormMain — main window

`FormMain.cs` / `FormMain.Designer.cs`. Client size 727×313, `CenterScreen`.

### 1.2.1 Server list — `listDescriptions`

`FormMain.Designer.cs:88-107`. `View=Details`, `FullRowSelect`, **`MultiSelect=false`**
(one server at a time), `SmallImageList = listImages`.

| Column | Field | Content | Where |
| --- | --- | --- | --- |
| **"Name"** | `colName` | `Description.Name` | `Designer:111`, `ServerListViewItem.cs:32` |
| **"Directories"** | `colDirectories`, right-aligned | **count** of directories, not their names | `Designer:115-116`, `ServerListViewItem.cs:33` |
| **"Active"** | `colActive` | the state enum name | `Designer:120`, `ServerListViewItem.cs:34` |

Row state — `ServerListViewItem.State` (`ServerListViewItem.cs:197-204`). The enum
value doubles as the image index into `listImages` (`ServerListViewItem.cs:161`),
whose keys are registered in order at `FormMain.cs:52-60`:

| State | Value | Text shown | Icon |
| --- | --- | --- | --- |
| `Idle` | 0 | `Idle` | `idle` |
| `Running` | 1 | `Running` | `active` |
| `Stopped` | 2 | `Stopped` | `inactive` |
| `Refreshing` | 3 | `Refreshing` | `refreshing` |
| `Loading` | 4 | `Loading` | `loading` |

`Idle` is only the pre-load value set in the constructor (`ServerListViewItem.cs:35`).

Events:
- `SelectedIndexChanged` → `ListDescriptions_SelectedIndexChanged`
  (`FormMain.cs:359-379`): enables/disables Start-Stop, Remove, Edit (button *and*
  context item together); relabels Start-Stop to **"Stop"** when the selected
  server is active and **"Start"** when it is not, swapping the icon between
  `inactive` and `active`; enables Rescan **only while the server is active**.
- `DoubleClick` → `listDescriptions_DoubleClick` (`FormMain.cs:349-357`): edits the
  selection if Edit is enabled, otherwise opens the New Server dialog.

### 1.2.2 Buttons

| Control | Label | Designer | Handler | Behaviour |
| --- | --- | --- | --- | --- |
| `buttonStartStop` | **"Start"** ⇄ **"Stop"** | `:212-224` | `ButtonStartStop_Click` `FormMain.cs:251-271` | Background task → `item.Toggle()`, then `SaveConfig()` and relabel both button and context item. Disabled initially |
| `buttonEdit` | **"Edit"** | `:198-210` | `ButtonEdit_Click` `FormMain.cs:181-198` | Modal `FormServer(item.Description)`; on OK runs `item.UpdateInfo(desc)` on a task, then `SaveConfig()`. Disabled initially |
| `buttonRemove` | **"Remove"** | `:226-238` | `buttonRemove_Click` `FormMain.cs:214-233` | Confirmation box, stop if active, drop from the list, `SaveConfig()`. Disabled initially |
| `buttonRescan` | **"Rescan"** | `:420-432` | `buttonRescan_Click` `FormMain.cs:235-249` | `item.Rescan()`; exceptions surface as a MessageBox. Disabled initially |
| `buttonNewServer` | **"New"** | `:185-196` | `ButtonNewServer_Click` `FormMain.cs:200-212` | Modal `FormServer()`; on OK builds a `ServerListViewItem`, adds it, `item.Load()`, `SaveConfig()` |

### 1.2.3 List context menu — `contextMenu`

`FormMain.Designer.cs:124-177`, attached to the list. In order:

1. **"Start/Stop"** (`ctxStartStop`) → `ButtonStartStop_Click`
2. **"Edit"** (`ctxEdit`) → `ButtonEdit_Click`
3. **"Remove"** (`ctxRemove`) → `buttonRemove_Click`
4. **"Rescan"** (`ctxRescan`) → `buttonRescan_Click`
5. — separator —
6. **"New Server"** (`ctxNewServer`) → `ButtonNewServer_Click`

Enablement and the Start/Stop relabel are shared with the buttons
(`FormMain.cs:363-374`).

### 1.2.4 Main menu — `mainMenu`

`FormMain.Designer.cs:288-418`. Two top-level items: **"&File"** and **"Help"**.

**&File** (`:302-316`):

| Label | Handler | Action |
| --- | --- | --- |
| **"New Server"** | `ButtonNewServer_Click` | Same as the New button |
| **"Settings"** | `settingsToolStripMenuItem_Click` `FormMain.cs:548-555` | Modal `FormSettings`, then `config.Save()` + `SetupLogging()` |
| **"Prevent sleep while playing"** | `preventSleepToolStripMenuItem_CheckedChanged` `FormMain.cs:118-124` | Checkable (`CheckOnClick`, `Designer:336`); writes `config.preventsleep`, saves, re-evaluates the sleep inhibitor. Initial state from settings (`FormMain.cs:70`) |
| — separator — | | |
| **"Open in Browser"** | `openInBrowserToolStripMenuItem_Click` `FormMain.cs:476-480` | Shells `http://localhost:{httpServer.RealPort}/` |
| **"Open Log Folder"** | `openLogFolderToolStripMenuItem_Click` `FormMain.cs:482-486` | Shells `CacheDir` |
| — separator — | | |
| **"Drop cache"** | `dropCacheToolStripMenuItem_Click` `FormMain.cs:273-303` | Confirm → stop every active server → delete `sdlna.cache` → restart them |
| — separator — | | |
| **"Hide"** | `hideToolStripMenuItem_Click` `FormMain.cs:339-342` | `WindowState = Minimized` (hides to tray) |
| **"&Exit"** | `exitContextMenuItem_Click` `FormMain.cs:305-310` | `canClose = true`, restore, `Close()` |

**Help** (`:397-418`):

| Label | Handler | Action |
| --- | --- | --- |
| **"Homepage"** | `homepageToolStripMenuItem_Click` `FormMain.cs:344-347` | `Process.Start("http://nmaier.github.io/simpleDLNA/")` — see §1.11 |
| **"About"** | `aboutToolStripMenuItem_Click` `FormMain.cs:174-179` | Modal `FormAbout` |

`Shell(string)` (`FormMain.cs:498-513`) is the safe opener: `UseShellExecute = true`,
logs and shows an error box on failure.

### 1.2.5 Status bar

`statusStrip` with a single `ToolStripStatusLabel statusPlayback`
(`Designer:434-449`), designer text **"Nothing playing"**.

`UpdatePlaybackState()` (`FormMain.cs:83-99`) is the single place playback state
turns into behaviour:

- playing → icon `active`, text `Playing: {session.Title} — {session.Client}`
  (`:92-93`), where `Client` is the client `IPAddress`
- otherwise → icon `idle`, text **"Nothing playing"** (`:96-97`)
- always → `sleepInhibitor.Inhibit = playing && config.preventsleep` (`:88`)

Driven by `httpServer.Playback.Changed` → `PlaybackChanged` (`FormMain.cs:101-116`),
marshalled with `BeginInvoke` and swallowing shutdown races.

### 1.2.6 Tray icon and its menu

`notifyIcon` (`Designer:240-243`), icon = the form icon (`FormMain.cs:66`),
double-click → `notifyIcon_DoubleClick` (`FormMain.cs:468-474`) which un-hides,
restores and re-adds to the taskbar.

Static menu `notifyContext` (`Designer:245-286`), in order:

1. **"Show"** → `notifyIcon_DoubleClick`
2. **"Rescan all"** → `rescanAllContextMenuItem_Click` (`FormMain.cs:515-525`),
   iterates every row and swallows per-item exceptions
3. `ContextSeperatorPre` — hidden when the list is empty
4. *(dynamic block)*
5. `ContextSeperatorPost`
6. **"Exit"** → `exitContextMenuItem_Click`

The dynamic block is rebuilt on every open by `notifyContext_Opening`
(`FormMain.cs:424-466`): all previously-inserted items (identified by a non-null
`Tag`) are removed, then one item labelled **`Rescan {server name}`** is inserted
for **each active server**, whose click calls `Rescan()` and swallows errors.

## 1.3 FormServer — add/edit a server

`FormServer.cs` / `.Designer.cs`. Modal, 544×508, `CenterParent`,
`ShowInTaskbar=false`, `MaximizeBox=false`, `AutoValidate=EnableAllowFocusChange`.

Title is **"New Server"** in the parameterless constructor (`FormServer.cs:20`) and
**"Edit Server"** from the designer otherwise (`Designer:484`).

### 1.3.1 "Name"

GroupBox **"Name"** (`Designer:78-98`) with `TextBox textName`.
Validator `textName_Validating` (`FormServer.cs:286-295`): blank → error
**"Must specify a name"**.

### 1.3.2 "Order"

GroupBox **"Order"** (`Designer:100-133`).

- `ComboBox comboOrder`, `DropDownList`. Filled by `AddOrderItems()`
  (`FormServer.cs:124-135`) from `ComparerRepository.ListItems()` ordered by key;
  the entry named `title` is preselected (`:131-133`). Display text is
  `BaseComparer.ToString()` = `"{Name} - {Description}"`
  (`server/Comparers/BaseComparer.cs:15`).

  | Value | Displayed as | Source |
  | --- | --- | --- |
  | `date` | `date - Sort by file date` | `server/Comparers/DateComparer.cs:7,9` |
  | `size` | `size - Sort by file size` | `server/Comparers/FileSizeComparer.cs:7,9` |
  | `title` | `title - Sort alphabetically` | `server/Comparers/TitleComparer.cs:11,13` |

- `CheckBox checkOrderDescending` — **"Descending"** (`Designer:113-122`) →
  `ServerDescription.OrderDescending`.

### 1.3.3 "Types"

GroupBox **"Types"** (`Designer:135-178`), three checkboxes → a `DlnaMediaTypes`
flags value composed in the `Description` getter (`FormServer.cs:85-94`):

| Checkbox | Label | Flag |
| --- | --- | --- |
| `checkVideo` | **"Video"** | `DlnaMediaTypes.Video` (1) |
| `checkAudio` | **"Audio"** | `DlnaMediaTypes.Audio` (4) |
| `checkImages` | **"Images"** | `DlnaMediaTypes.Image` (2) |

Validator `checkTypes_Validating` (`FormServer.cs:229-238`), attached to the group
box (`Designer:148`): none checked → error **"Must select at least one"**.
New servers default to Video only (`FormServer.cs:21`).

### 1.3.4 Tab "Views"

`tabPageViews` (`Designer:284-377`), tab label **"Views"**.

- `ComboBox comboNewView`, `DropDownList`, filled by `AddViewItems()`
  (`FormServer.cs:137-145`) from `ViewRepository.ListItems()` ordered by key.
  Display text is `BaseView.ToString()` = `"{Name} - {Description}"`.

  | Value | Description (verbatim) | Source |
  | --- | --- | --- |
  | `bytitle` | Reorganizes files into folders by title | `server/Views/ByTitleView.cs:10,12` |
  | `dimension` | Show only items of a certain dimension | `DimensionView.cs:21,23` |
  | `filter` | Show only files matching a specific filter | `FilterView.cs:13,15` |
  | `flatten` | Removes empty intermediate folders and flattens folders with only few files | `FlattenView.cs:7,9` |
  | `large` | Show only large files | `LargeView.cs:11,13` |
  | `music` | Reorganizes files into a proper music collection | `MusicView.cs:9,11` |
  | `new` | Show only new files | `NewView.cs:11,13` |
  | `plain` | Mushes all files together into the root folder | `PlainView.cs:8,10` |
  | `series` | Try to determine (TV) series from title and categorize accordingly | `SeriesView.cs:19,21` |
  | `sites` | Try to determine websites from title and categorize accordingly | `SiteView.cs:21,23` |

- `ListView listViews`, `Details`, columns **"Name"** (`colViewName`) and
  **"Description"** (`colViewDesc`) (`Designer:369-377`).
- **"Add"** (`buttonAddView`) → `buttonAddView_Click` (`FormServer.cs:190-199`) —
  appends `{Name, Description}`. **Duplicates are not prevented.**
- **"Remove"** (`buttonRemoveView`) → `buttonRemoveView_Click` (`:221-227`)
- **"Up"** (`buttonViewUp`, `Designer:321-329`) and **"Down"** (`buttonViewDown`,
  `Designer:311-319`) — **no Click handler is wired; both buttons do nothing.**
  See §1.11.

Only the bare view name is persisted (`FormServer.cs:95-96`), so view order is
insertion order.

### 1.3.5 Tab "Restrictions"

`tabPageRestrictions` (`Designer:379-463`), tab label **"Restrictions"**.

- `TextBox textRestriction` — the value.
- `ComboBox comboNewRestriction`, `DropDownList`, **hardcoded** items
  (`Designer:408-411`): **"MAC"**, **"IP"**, **"User-Agent"**. Defaults to index 0
  (`FormServer.cs:251`).
- `ListView listRestrictions`, columns **"Restriction"** (`colRestriction`) and
  **"Type"** (`colRestrictionType`); each row's `Tag` holds the type index 0/1/2.
- **"Add"** (`buttonAddRestriction`) → `buttonAddRestriction_Click`
  (`FormServer.cs:161-188`), validated per type:

  | Type | Rule | Where |
  | --- | --- | --- |
  | MAC | `IP.IsAcceptedMAC(value)` — form `01:AF:BC:00:0A:FF` | `FormServer.cs:167` |
  | IP | `IPAddress.TryParse` | `:170-171` |
  | User-Agent | non-blank | `:174` |

  Invalid → error **"You must provide a valid value"** (`:180-181`).
- **"Remove"** (`buttonRemoveRestriction`) → `buttonRemoveRestriction_Click`
  (`:209-219`) — removes the row **and puts its value and type back into the
  textbox/combo** for re-editing.

Mapped to `Macs` / `Ips` / `UserAgents` by tag in the `Description` getter
(`FormServer.cs:99-107`).

### 1.3.6 "Directories"

GroupBox **"Directories"** (`Designer:180-245`).

- `ListView listDirectories`, `Details`, one column **"Directory"**
  (`colDirectory`), `Sorting = Ascending` (`Designer:236`).
- **"Add"** (`buttonAddDirectory`) → `buttonAddDirectory_Click`
  (`FormServer.cs:147-159`): opens a `FolderBrowserDialog` (`folderDialog`,
  `Designer:49`) and de-duplicates with `StringComparer.InvariantCulture`.
- **"Remove"** (`buttonRemoveDirectory`) → `buttonRemoveDirectory_Click` (`:201-207`).
- Validator `listDirectories_Validating` (`:254-264`): zero rows → error
  **"Must specify at least one directory"**, anchored on the invisible
  `listDirectoriesAnchor` label.

### 1.3.7 Dialog buttons and validation

- **"&OK"** (`buttonAccept`, `DialogResult.OK`, `AcceptButton`) and **"&Cancel"**
  (`buttonCancel`, `DialogResult.Cancel`, `CancelButton`) — `Designer:247-267`.
- `FormServer_FormClosing` (`FormServer.cs:240-243`): OK is blocked while any
  validator fails (`e.Cancel = DialogResult == OK && !ValidateChildren()`).
- `ErrorProvider errorProvider`, `BlinkStyle = NeverBlink` (`Designer:269-272`).
- The result is a **fresh** `ServerDescription` built by the `Description` getter
  (`FormServer.cs:82-122`). It deliberately does not set `Active`; `AdoptInfo`
  (`ServerDescription.cs:34-48`) also does not copy `Active`, so a running server
  stays running across an edit.

## 1.4 FormSettings — global preferences

`FormSettings.cs` / `.Designer.cs`. Modal, 331×364, `FixedDialog`,
`ShowInTaskbar=false`, title **"Settings"** (`Designer:269`).

**There is no Cancel button.** Every control is data-bound with
`DataSourceUpdateMode.OnPropertyChanged`, so edits commit immediately;
`FormMain` persists them when the dialog closes (`FormMain.cs:550-554`).

| Group | Control | Label / unit | Bound to | Range | Where |
| --- | --- | --- | --- | --- | --- |
| **"Port"** | `numericPort` | — | `Settings.port` | 0–65535 | `Designer:58-82` |
| **"Cache directory"** | `textCacheFile` | — | `Settings.cache` | — | `Designer:84-93` |
| | `buttonBrowseCacheFile` | **"Browse"** | — | — | `Designer:106-114`, handler `FormSettings.cs:38-43` (`FolderBrowserDialog`) |
| **"Library refresh"** | `numericRescanDelay` | **"seconds after a change is detected"** | `Settings.rescandelay` | 1–3600 | `Designer:129-158` |
| | `numericRescanInterval` | **"minutes between full rescans (0 = off)"** | `Settings.rescaninterval` | 0–1440 | `Designer:160-184` |
| **"Logging"** | `comboLogLevel` | **"detail written to sdlna.log"** | `Settings.loglevel` (in code, not bound) | see below | `Designer:209-228`, `FormSettings.cs:23-27,45-50` |
| — | `checkStartMinimized` | **"Start minimized"** | `Settings.startminimized` | — | `Designer:186-196` |
| — | `checkAutoStart` | **"Start automatically with Windows"** | **registry**, not settings | — | `Designer:241-250`, `FormSettings.cs:52-60` |
| — | `buttonOK` | **"OK"** | — | — | `Designer:230-239`, `DialogResult.OK`, no handler |

Tooltips (verbatim, `ToolTip toolTip`):

- Port — *"Port of the http server.\nLeave at 0 to automatically have a port
  selected on startup.\n\n(Requires restart)"* (`Designer:80-81`)
- Cache directory — *"Location of the cache directory.\nLeave blank to use the
  default location (TEMP).\n\n(Requires restart)"* (`Designer:92-93`)
- Rescan delay — *"How long to wait after a file or folder changes before
  rescanning.\nChanges arriving during the wait are batched into one rescan.\n\n
  (Applies when a server is restarted)"* (`Designer:155-157`)
- Rescan interval — *"Safety net for changes the file watcher cannot see, such as
  edits made\non a network share. Set to 0 to rely on the watcher alone.\n\n
  (Applies when a server is restarted)"* (`Designer:181-183`)
- Log level — *"How much detail to write to sdlna.log in the cache directory.\n
  None turns logging off entirely. Debug is very noisy."* (`Designer:217-218`)

**Log levels**, coarsest first (`FormMain.cs:560-563`), default `"Error"`
(`FormMain.cs:565`): **None, Fatal, Error, Warn, Info, Debug**. The combo is
populated in code rather than bound so an unknown stored value falls back to the
default instead of being silently kept (`FormSettings.cs:20-27`). Mapping to
log4net levels is `ToLog4NetLevel` (`FormMain.cs:567-583`) — note `"Error"` is the
`default:` arm and `"None"` maps to `Level.Off`.

**Autostart** writes the registry immediately on toggle, with no OK required
(`FormSettings.cs:52-60`). On Mono the checkbox is hidden entirely (`:33-35`).

## 1.5 FormAbout

`FormAbout.cs:10-20`. Title `About {ProductInformation.Title}`. Shows the banner
image, product title (bold), `Version {ProductVersion}`, copyright (italic), and
the embedded `LICENSE` in a read-only multiline textbox. One button, **"&OK"**.
`ProductInformation` reads the assembly attributes (`util/ProductInformation.cs`).

## 1.6 Message boxes

Every modal confirmation/error in the app:

| Title | Text | Buttons | Where |
| --- | --- | --- | --- |
| **"Remove Server"** | `Would you like to remove {name}?` | Yes/No, Question | `FormMain.cs:220-224` |
| **"Drop cache"** | `Are you sure you want to drop the cache?` | Yes/No, Warning | `FormMain.cs:275-280` |
| **"Error"** | the rescan exception message | OK, Error | `FormMain.cs:245-247` |
| **"Error"** | `Could not open {target}\n\n{message}` | OK, Error | `FormMain.cs:509-511` |
| **"Error"** | `Encountered an unhandled error. Will exit now.\n\n{message}\n{stack}` | OK, Error | `Program.cs:37-44` |

## 1.7 The configuration surface

### 1.7.1 Per-server — `ServerDescription`

`ServerDescription.cs:14-32`. This is the entire per-server model:

| Property | Type | Set from |
| --- | --- | --- |
| `Active` | `bool` | Start/Stop only — **never** from the editor |
| `Name` | `string` | `textName` |
| `Directories` | `string[]` | `listDirectories` |
| `Order` | `string` | `comboOrder` (comparer name) |
| `OrderDescending` | `bool` | `checkOrderDescending` |
| `Types` | `DlnaMediaTypes` flags | `checkVideo/Audio/Images` |
| `Views` | `string[]` | `listViews`, insertion-ordered |
| `Macs` | `string[]` | Restrictions, tag 0 |
| `Ips` | `string[]` | Restrictions, tag 1 |
| `UserAgents` | `string[]` | Restrictions, tag 2 |

`AdoptInfo` (`:34-48`) copies everything **except `Active`**. `ToggleActive`
(`:50-53`) flips it. There is **no id field** — `Name` is the de-facto key.

### 1.7.2 Global — `Properties.Settings`

`Properties/Settings.Designer.cs` + the hand-written partial `Settings.cs`. All
`[UserScopedSetting]`, so they live in `user.config`, **not** in `CacheDir`.

| Key | Type | Default | Line |
| --- | --- | --- | --- |
| `cache` | string | `""` | `:26-36` |
| `port` | decimal | `0` | `:38-48` |
| `MustUpgrade` | bool | `True` | `:50-60` |
| `preventsleep` | bool | `False` | `:62-72` |
| `loglevel` | string | `Error` | `:74-84` |
| `startminimized` | bool | `False` | `:86-96` |
| `rescandelay` | decimal | `5` | `:98-108` |
| `rescaninterval` | decimal | `30` | `:110-120` |
| `Descriptors` | `List<ServerDescription>` | `""` | `Settings.cs:22-34` (legacy) |

The constructor (`Settings.cs:9-20`) runs `Upgrade()` once when `MustUpgrade` is
set, to pull settings forward from a previous version. All exceptions swallowed.

### 1.7.3 Cache directory resolution

`FormMain.CacheDir` (`FormMain.cs:131-163`), in order: `config.cache` if non-blank
**and the directory exists** → `%LOCALAPPDATA%\SimpleDLNA` → `%APPDATA%\SimpleDLNA`
→ `Path.GetTempPath()`. It holds `sdlna.cache` (`:34-35`), `sdlna.log` (`:37-38`)
and `descriptors.xml` (`:28`).

## 1.8 Persistence

Three independent stores:

1. **`descriptors.xml`** — the source of truth for servers.
   `Path.Combine(CacheDir, "descriptors.xml")`, `XmlSerializer` over
   `ServerDescription[]` on write (`FormMain.cs:527-546`) and
   `List<ServerDescription>` on read (`:406-422`). Written to `.tmp` then
   `File.Copy(overwrite: true)`; failures are logged only.
2. **`user.config`** — the global settings above, via `ApplicationSettingsBase`.
   Saved at `FormMain.cs:122` (sleep toggle), `:545` (end of `SaveConfig`) and
   `:552` (settings dialog closed).
3. **Registry** — `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`, value name
   **`SimpleDLNA`**, data `Application.ExecutablePath`
   (`StartUpUtilities.cs:21,49-62`, `FormSettings.cs:11,30`).

**Legacy migration:** `LoadDescriptors` falls back to the `config.Descriptors`
list if `descriptors.xml` cannot be read (`FormMain.cs:416-418`), and `LoadConfig`
then clears that list and re-saves (`:400-401`) — a one-way move out of
`user.config`.

Startup load (`LoadConfig`, `FormMain.cs:381-404`) adds every row, then loads them
in parallel with `MaxDegreeOfParallelism = min(2, ProcessorCount)`.

## 1.9 Server lifecycle

`ServerListViewItem.cs` — the GUI's controller, mixed into a `ListViewItem`.

**`StartFileServer()`** (`:75-138`):

1. If `!Description.Active` → state `Stopped`, return. *(A newly created server is
   therefore inactive until Start is pressed.)*
2. State `Loading`.
3. `new Identifiers(ComparerRepository.Lookup(Description.Order), Description.OrderDescending)`
   then `ids.AddView(v)` per stored view name (`:84-87`).
4. Filter `Directories` down to those that exist; none → throw
   `InvalidOperationException("No remaining directories")` (`:88-94`).
5. `new FileServer(Types, ids, dirs)` with `FriendlyName = Name`,
   `ChangeDelay = rescandelay` seconds, `RescanInterval = rescaninterval` minutes
   (`:95-103`).
6. `#if !DEBUG` → `SetCacheFile(cacheFile)` (`:104-108`).
7. Subscribe `Changing` → `Refreshing`, `Changed` → `Running`/`Stopped` (`:109-110`).
8. `fileServer.Load()`.
9. Build an `HttpAuthorizer`, adding `IPAddressAuthorizer` / `MacAuthorizer` /
   `UserAgentAuthorizer` **only for non-empty lists** (`:112-122`).
10. `server.RegisterMediaServer(fileServer)` → state `Running`; log at `Notice`:
    `{FriendlyName} loaded in {seconds:F2} seconds` (`:123-131`).
11. **On any exception** — log the error, `Description.ToggleActive()` (flip back to
    inactive) and state `Stopped` (`:133-137`).

Other operations:

| Operation | Where | Semantics |
| --- | --- | --- |
| `Load()` | `:165-169` | State `Loading`, then start |
| `StopFileServer()` | `:140-150` | `UnregisterMediaServer`, dispose, state `Stopped` |
| `Toggle()` | `:183-188` | Stop → flip `Active` → start |
| `UpdateInfo(desc)` | `:190-195` | Stop → `AdoptInfo` → start — **editing restarts the server** |
| `Rescan()` | `:171-181` | Requires a live `fileServer` cast to `IVolatileMediaServer`; else `ArgumentException` **"Server is not running"** / **"Server does not support rescanning"** |

All UI mutation from server threads goes through `ServerListViewItem.BeginInvoke`
(`:55-73`), which also re-auto-sizes every column afterwards.

One `HttpServer` is shared by the whole app, created in `SetupServer()`
(`FormMain.cs:632-637`) as `new HttpServer((int)config.port)`; `RealPort` is then
appended to the title. **Port and cache directory cannot change without an app
restart** — the listener is created once.

## 1.10 Logging

Configured entirely in code by `SetupLogging()` (`FormMain.cs:591-630`), called
from the constructor (`:62`) and again whenever the settings dialog closes (`:553`).

- `hierarchy.ResetConfiguration()` first, otherwise each visit stacks another
  appender and every line is written N times (`:596`).
- `Level.Off` short-circuits: root level and threshold both `Off` (`:599-604`).
- Otherwise a `RollingFileAppender` on `{CacheDir}\sdlna.log`: composite rolling,
  `DatePattern "'.'yyyy-MM-dd"`, `MaximumFileSize "5MB"`, `MaxSizeRollBackups 1`,
  `StaticLogFileName`, `PreserveLogFileNameExtension`, `ImmediateFlush`
  (`:611-625`).
- Pattern: `%date %6level [%3thread] %-30.30logger{1} - %message%newline%exception`.

**There is no in-app log viewer.** The only affordances are the level combo and
File → "Open Log Folder".

## 1.11 Known gaps and defects

Recorded so they are not faithfully reproduced in the web UI.

1. **View reordering does not exist.** `buttonViewUp` (`FormServer.Designer.cs:321-329`)
   and `buttonViewDown` (`:311-319`) have no `Click` handler. The buttons are
   visible, enabled and inert.
2. **Parameterised views are unreachable from the GUI.** `util/Repository.cs:45-81`
   supports `name:param=value` (e.g. `large:size=700`), and the CLI uses it, but the
   editor stores only the bare name (`FormServer.cs:95-96`). Worse, a
   parameterised name loaded from `descriptors.xml` makes the edit constructor
   throw at `ViewRepository.Lookup(v).Description` (`FormServer.cs:51`).
3. **Duplicate views can be added** — `buttonAddView_Click` (`:190-199`) does not
   de-duplicate, unlike the directory list.
4. **`cache` is used two ways.** It is treated as a *directory* by `CacheDir`
   (`FormMain.cs:134-136`) and the Browse button (`FormSettings.cs:40-41`), but as a
   *file path* for the media cache (`FormMain.cs:67-69`).
5. **`LoadConfig` can NRE.** `config.Descriptors.Clear()` (`FormMain.cs:400`) has no
   null check, and `Descriptors` is null on a profile that never persisted it.
6. **"Homepage" is broken on .NET 5+.** `homepageToolStripMenuItem_Click`
   (`FormMain.cs:346`) calls bare `Process.Start(url)` without
   `UseShellExecute = true`, unlike the `Shell()` helper right next to it
   (`:498-513`), so it throws `Win32Exception`.
7. **No notifications.** No balloon tips or toasts exist; the only status surfaces
   are the tray tooltip (the window title) and the `statusPlayback` label.
8. **Single selection only.** `MultiSelect=false` (`FormMain.Designer.cs:99`) — no
   bulk start/stop/remove.
9. **No per-server rescan feedback.** Tray "Rescan {name}" and "Rescan all"
   swallow every exception (`FormMain.cs:452-457`, `:518-523`).

## 1.12 Parity checklist

Every user-facing capability, flat. §3 must map each row to a replacement or an
explicit drop.

| # | Capability | Source |
| --- | --- | --- |
| 1 | List configured servers with name, directory count and state | §1.2.1 |
| 2 | See live state transitions (Loading → Running → Refreshing → Stopped) | §1.2.1 |
| 3 | Create a server | §1.2.2 New |
| 4 | Edit a server (restarts it if running) | §1.2.2 Edit |
| 5 | Remove a server, with confirmation | §1.2.2 Remove |
| 6 | Start / stop a server | §1.2.2 Start-Stop |
| 7 | Rescan one server (only while running) | §1.2.2 Rescan |
| 8 | Rescan all servers | §1.2.6 tray |
| 9 | Rescan a specific server from the tray | §1.2.6 dynamic items |
| 10 | Set server name | §1.3.1 |
| 11 | Choose sort order from the comparer registry | §1.3.2 |
| 12 | Toggle descending sort | §1.3.2 |
| 13 | Choose media types (video / audio / images), at least one | §1.3.3 |
| 14 | Add / remove views from the view registry | §1.3.4 |
| 15 | Add / remove MAC, IP and User-Agent restrictions, validated | §1.3.5 |
| 16 | Add / remove media directories via a folder picker | §1.3.6 |
| 17 | Validation feedback before the dialog can be accepted | §1.3.7 |
| 18 | Set the HTTP port (0 = auto), restart required | §1.4 |
| 19 | Set the cache directory, restart required | §1.4 |
| 20 | Set rescan delay (1–3600 s) | §1.4 |
| 21 | Set rescan interval (0–1440 min, 0 = off) | §1.4 |
| 22 | Set log level (None…Debug) | §1.4 |
| 23 | Toggle start-minimized | §1.4 |
| 24 | Toggle autostart with Windows | §1.4 |
| 25 | Toggle prevent-sleep-while-playing | §1.2.4 |
| 26 | See current playback (title + client) or "Nothing playing" | §1.2.5 |
| 27 | Open the DLNA browse UI in a browser | §1.2.4 |
| 28 | Open the log folder | §1.2.4 |
| 29 | Drop the media cache, with confirmation | §1.2.4 |
| 30 | See product name, version, copyright and licence | §1.5 |
| 31 | Open the project homepage | §1.2.4 (broken, §1.11 #6) |
| 32 | Hide to tray / show from tray / exit | §1.1, §1.2.6 |
| 33 | Single-instance: a second launch focuses the first | §1.1 |
| 34 | See the active port at a glance (title / tray tooltip) | §1.1 |

---

# §2 — REST API design

The control plane for everything in §1.12, at `http://127.0.0.1:19199/api/v1`,
hosted in the same process as the DLNA server.

## 2.1 Change from the plan: a dedicated admin listener

`MIGRATION-PLAN.md` §4 Step 2 proposed *"a loopback `TcpListener` on 19199,
reusing the existing `HttpClient` parse/response machinery"* and making
`server/`'s handler interfaces public. **Reading the stack closely says
otherwise.** Eight properties of `server/Http` are wrong for a JSON control API,
and six of them are wrong in ways that would require editing the code path that
streams media to TVs:

| # | Property | Evidence | Why it blocks reuse |
| --- | --- | --- | --- |
| 1 | The constructor unconditionally starts SSDP | `HTTPServer.cs:72` | A second `HttpServer` would run a second SSDP responder on :1900 and advertise a duplicate device set |
| 2 | Binds `IPAddress.Any`, no bind parameter | `HTTPServer.cs:63` | Loopback-only is not expressible |
| 3 | `/` is hard-coded to `IndexHandler` | `HTTPServer.cs:207-209` | The SPA cannot own the root path |
| 4 | **Request bodies are ASCII-lossy** | `HttpClient.cs:259` | See §2.13 #1 — every non-ASCII character in a POST/PUT body becomes `?` |
| 5 | `Path` is never split on `?` nor URL-decoded | `HttpClient.cs:268` | No query strings, no encoded path segments |
| 6 | `HttpCode` has no `201/204/400/409/422`, and `HttpPhrases.Phrases[status]` is indexed unconditionally | `HttpCode.cs:3-17`, `HttpClient.cs:322` | An unlisted code throws `KeyNotFoundException` while writing the status line |
| 7 | Every response is a `ConcatenatedStream` with a computed `Content-Length` | `HttpClient.cs:316-331`, `:139-157` | No open-ended responses, so no SSE |
| 8 | `IHandler`, `IPrefixHandler`, `IResponse`, `RegisterHandler` are `internal` | `Interfaces/IHandler.cs:3`, `HTTPServer.cs:216` | Any external handler needs the surface made public |

**Decision: give the admin API its own minimal HTTP layer inside the new
`admin/` project, and leave `server/Http` untouched.**

The admin surface needs correct UTF-8 bodies, query strings, the full REST status
vocabulary and open-ended responses — four things the media stack does not have
and does not need. A purpose-built loopback listener is roughly 250 lines, is
correct by construction, carries **zero** risk to media serving, and requires no
visibility changes in `server/`. Reuse would mean rewriting request parsing and
response writing underneath the DLNA path to gain code we would then have to
special-case anyway.

What we still reuse, unchanged and public already: `HttpServer` itself
(`RegisterMediaServer` / `UnregisterMediaServer` / `RealPort` / `MediaMounts` /
`Playback`), `FileServer`, `Identifiers`, `ViewRepository`, `ComparerRepository`,
and the `HttpAuthorizer` family.

*(Rejected alternative: add a "no SSDP, bind address, extra status codes" mode to
`HttpServer` and make the handler interfaces public. Fewer lines overall, but it
edits the media path to serve an admin concern and still leaves #4/#5/#7 to fix.)*

## 2.2 Architecture

```
util  ─→  server  ─→  fsserver  ─┐
                                 ├─→  admin (SimpleDlna.Admin)  ─→  sdlna.exe
                                 ┘                               └─→  SimpleDLNA.exe
```

New project `admin/`, assembly `SimpleDlna.Admin`, referencing `server` and
`fsserver` — the first project allowed to know about both. It contains:

| Piece | Responsibility |
| --- | --- |
| `ServerManager` | Owns the configured servers, their `FileServer` instances, their state, and `descriptors.xml`. UI-free. |
| `SettingsStore` | Global settings, backed by `settings.json` (§2.12) |
| `AdminServer` | Loopback listener + router |
| `ApiHandlers` | One method per endpoint, translating JSON ⇄ `ServerManager` |
| `WebAssets` | Serves the embedded SPA (specified in §4) |

### 2.2.1 `ServerManager` — extracting the GUI's controller

`SimpleDLNA/ServerListViewItem.cs:75-195` is the existing implementation of
exactly this logic, welded to a `ListViewItem`. It moves across essentially
unchanged; only the `BeginInvoke` marshalling (`:55-73`) and the `SubItems`
rendering (`:152-163`) are dropped, replaced by a `StateChanged` event.

```csharp
public sealed class ManagedServer {
  public Guid Id { get; }
  public ServerDescription Description { get; }
  public ServerState State { get; }          // idle|loading|running|refreshing|stopped
  public string LastError { get; }           // null unless the last start threw
  public DateTime? StartedUtc { get; }
  public double? LoadSeconds { get; }        // from the existing Notice log line
  public string MountPrefix { get; }         // "/mm-3/", null when stopped
}

public sealed class ServerManager : IDisposable {
  IReadOnlyList<ManagedServer> Servers { get; }
  ManagedServer Add(ServerDescription d);
  ManagedServer Update(Guid id, ServerDescription d);   // stop → AdoptInfo → start
  void Remove(Guid id);
  void Start(Guid id); void Stop(Guid id);
  void Rescan(Guid id); void RescanAll();
  void DropCache();
  event EventHandler<ServerStateChangedEventArgs> StateChanged;
}
```

Semantics are inherited verbatim from §1.9 and must not drift:

- `Update` stops, adopts and restarts — editing a running server restarts it.
- `AdoptInfo` never copies `Active` (`ServerDescription.cs:34-48`), so a running
  server stays running across an edit.
- A failed start logs, flips `Active` back to false and lands in `Stopped`
  (`ServerListViewItem.cs:133-137`). **New:** the exception message is also kept
  in `LastError` so the UI can show *why*, which the GUI never did.
- All mutations serialize under one lock. `Start`/`Stop`/`Rescan` are slow
  (`FileServer.Load()` walks the tree), so the API runs them on the thread pool
  and returns `202` — see §2.6.3.

### 2.2.2 Server identity

`ServerDescription` has no id (§1.7.1); `Name` is the de-facto key. Add:

```csharp
public Guid Id { get; set; }   // XmlSerializer round-trips it as an element
```

Generated on load when absent (`Guid.Empty`), so existing `descriptors.xml` files
keep working and gain ids on first save. `Name` stays free-form and duplicable.

Note `FileServer.UUID` is **not** usable as the API id: it is derived from the
directory-based friendly name with only one random byte left over
(`fsserver/FileServer.cs:150-163`), it only exists while the server is running,
and two configurations over the same directory collide with probability 1/256 —
at which point `RegisterMediaServer` throws *"Attempting to register more than
once"* (`HTTPServer.cs:257-259`). The API reports it read-only as `uuid`.

## 2.3 Conventions

| Aspect | Rule |
| --- | --- |
| Base path | `/api/v1` — version in the path, bumped only on a breaking change |
| Encoding | UTF-8 in and out; `Content-Type: application/json; charset=utf-8` |
| Casing | `camelCase` members; enums as lowercase strings (`"running"`, `"video"`) |
| Timestamps | ISO-8601 UTC, e.g. `2026-08-11T09:12:33Z` |
| Unknown fields | Rejected with `422`, not ignored — a typo in a view name should not silently disable it |
| Partial updates | Not supported. `PUT` replaces the whole resource; there is no `PATCH` |
| Caching | `Cache-Control: no-store` on every `/api/v1` response |
| Method override | None. Real verbs only |

### 2.3.1 Status codes

| Code | Used for |
| --- | --- |
| `200 OK` | Successful read, or a mutation that completed synchronously |
| `201 Created` | `POST /servers`, with `Location` |
| `202 Accepted` | Start / stop / rescan / drop-cache — work continues in the background |
| `204 No Content` | `DELETE` |
| `400 Bad Request` | Malformed JSON, bad query parameter |
| `404 Not Found` | Unknown id or route |
| `409 Conflict` | Illegal transition for the current state (e.g. start while `loading`) |
| `422 Unprocessable Entity` | Well-formed JSON that fails validation |
| `500 Internal Server Error` | Anything unhandled |

### 2.3.2 Error shape

Every non-2xx response carries:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "The server description is not valid.",
    "details": [
      { "field": "name",        "message": "Must specify a name" },
      { "field": "directories", "message": "Must specify at least one directory" }
    ]
  }
}
```

`code` is a stable machine-readable slug; `message` is human-readable; `details`
is present only for `422`. The `field`/`message` pairs reproduce the GUI's
validator strings verbatim (§1.3), so the SPA can show the same wording.

Codes: `bad_json`, `bad_parameter`, `not_found`, `conflict`,
`validation_failed`, `restart_required`, `io_error`, `internal_error`.

## 2.4 `GET /api/v1/status`

Covers checklist rows 26 and 34.

```json
{
  "version": "1.2.0",
  "signature": "WIN64/10.0 UPnP/1.0 DLNADOC/1.5 sdlna/1.2",
  "mediaPort": 49312,
  "adminPort": 19199,
  "startedUtc": "2026-08-11T08:02:11Z",
  "cacheDir": "C:\\Users\\ilya\\AppData\\Local\\SimpleDLNA",
  "browseUrl": "http://localhost:49312/",
  "host": "tray",
  "playback": {
    "playing": true,
    "title": "Blade Runner",
    "client": "192.168.1.44",
    "mediaType": "video",
    "startedUtc": "2026-08-11T09:10:02Z"
  },
  "serverCount": { "total": 3, "running": 2 }
}
```

Sources: `HttpServer.RealPort` (`HTTPServer.cs:91`), `HttpServer.Signature`
(`:17`), `Playback.IsPlaying` / `.Current` (`PlaybackMonitor.cs:86,99`),
`ProductInformation`, `FormMain.CacheDir` logic (`FormMain.cs:131-163`, moving
into `SettingsStore`). `playback` is `null` when idle, matching *"Nothing
playing"*. `host` is `"tray"` or `"console"` so the SPA can hide tray-only
settings (§2.7).

## 2.5 `GET /api/v1/capabilities`

Covers rows 11 and 14 — the SPA must not hard-code the view and order lists.

```json
{
  "orders": [
    { "name": "date",  "description": "Sort by file date" },
    { "name": "size",  "description": "Sort by file size" },
    { "name": "title", "description": "Sort alphabetically", "default": true }
  ],
  "views": [
    { "name": "bytitle", "description": "Reorganizes files into folders by title",
      "configurable": false, "parameters": [] },
    { "name": "large",   "description": "Show only large files",
      "configurable": true,
      "parameters": [ { "name": "size", "type": "uint", "unit": "MB", "default": 300 } ] }
  ],
  "mediaTypes":       [ "video", "audio", "image" ],
  "restrictionTypes": [ "mac", "ip", "userAgent" ],
  "logLevels":        [ "None", "Fatal", "Error", "Warn", "Info", "Debug" ]
}
```

`orders` and `views` come straight from `ComparerRepository.ListItems()` and
`ViewRepository.ListItems()` (`util/Repository.cs:40-43`) — the same call the GUI
already makes (`FormServer.cs:124-145`), so new views appear automatically.

**`parameters` has no runtime source.** `IRepositoryItem` exposes only `Name` and
`Description` (`util/IRepositoryItem.cs:3-8`); each view's accepted parameters are
implicit in its `SetParameters` body. Two options, decide during implementation:

- **(a) A static table in `admin/`** listing the parameters of the four
  configurable views (`dimension`, `filter`, `large`, `new`), keyed by name.
  Cheap; drifts if a view changes.
- **(b) An optional `IParameterDescribing` interface** on `IView`, implemented by
  those four. Self-maintaining; touches `server/`.

Recommend (b) — it is four small additions and removes a drift class. Ship (a)
only if `server/` must stay frozen.

## 2.6 Servers

### 2.6.1 Resource

```json
{
  "id": "0f3d…", "name": "Movies",
  "active": true, "state": "running", "lastError": null,
  "order": "title", "orderDescending": false,
  "types": ["video"],
  "views": ["series", "large:size=700"],
  "directories": ["D:\\Media\\Movies", "E:\\More"],
  "restrictions": {
    "mac": ["01:AF:BC:00:0A:FF"], "ip": ["192.168.1.44"], "userAgent": []
  },
  "uuid": "73646c6e-…", "mountPrefix": "/mm-3/",
  "startedUtc": "2026-08-11T08:02:19Z", "loadSeconds": 4.72
}
```

Read-only: `id`, `state`, `lastError`, `uuid`, `mountPrefix`, `startedUtc`,
`loadSeconds`. `active` is read-only on `PUT` (changed only via start/stop),
mirroring `AdoptInfo`.

`state` ∈ `idle | loading | running | refreshing | stopped`, exactly the GUI's
enum (`ServerListViewItem.cs:197-204`).

`views` entries are the raw strings `Identifiers.AddView` consumes, so the
`name:param=value` form (`util/Repository.cs:52-64`) is expressible — closing
§1.11 #2. `types` is an array rather than the `DlnaMediaTypes` bitmask.

### 2.6.2 CRUD

| Verb | Path | Result | Checklist |
| --- | --- | --- | --- |
| `GET` | `/servers` | `{ "servers": [ … ] }` | 1, 2 |
| `POST` | `/servers` | `201` + `Location: /api/v1/servers/{id}` | 3 |
| `GET` | `/servers/{id}` | `200` / `404` | — |
| `PUT` | `/servers/{id}` | `200`; restarts if running | 4 |
| `DELETE` | `/servers/{id}` | `204`; stops first if running | 5 |

Validation on `POST`/`PUT`, reproducing §1.3 exactly:

| Field | Rule | `422` message |
| --- | --- | --- |
| `name` | non-blank | `Must specify a name` |
| `types` | ≥ 1 entry, each in `capabilities.mediaTypes` | `Must select at least one` |
| `directories` | ≥ 1 entry | `Must specify at least one directory` |
| `order` | resolves via `ComparerRepository` | `Unknown sort order '{v}'` |
| `views[]` | each resolves via `ViewRepository.Lookup` | `Unknown view '{v}'` |
| `restrictions.mac[]` | `IP.IsAcceptedMAC` | `You must provide a valid value` |
| `restrictions.ip[]` | `IPAddress.TryParse` | `You must provide a valid value` |
| `restrictions.userAgent[]` | non-blank | `You must provide a valid value` |

Directories are **not** checked for existence at validation time — `FileServer`
filters non-existent ones at start and only fails when none remain
(`ServerListViewItem.cs:88-94`). The API reports that as `lastError`. Duplicate
directories are de-duplicated (`FileServer` already calls `.Distinct()`,
`FileServer.cs:69`); duplicate views are rejected with `422`, closing §1.11 #3.

### 2.6.3 Actions

| Verb | Path | Result | Checklist |
| --- | --- | --- | --- |
| `POST` | `/servers/{id}/start` | `202` + resource in `loading` | 6 |
| `POST` | `/servers/{id}/stop` | `202` + resource in `stopped` | 6 |
| `POST` | `/servers/{id}/rescan` | `202` | 7 |
| `POST` | `/servers/rescan-all` | `202` + `{ "requested": 2, "skipped": 1 }` | 8, 9 |

All are asynchronous: the manager flips state, queues the work and returns
immediately. Progress arrives over `/events` (§2.10) or by re-reading the
resource. This mirrors the GUI, which already runs `Toggle()` on a task
(`FormMain.cs:257-270`) — a synchronous API would hold the request open for the
whole library scan.

Conflicts (`409`): starting something already `running`/`loading`, stopping
something already `stopped`, or rescanning a server that is not `running`. The
last reproduces the GUI's `ArgumentException("Server is not running")`
(`ServerListViewItem.cs:171-181`) as a status code instead of a MessageBox.

`rescan-all` skips non-running servers rather than failing, matching the GUI's
swallow-everything loop (`FormMain.cs:515-525`) — but it *reports* the skip count
instead of hiding it (§1.11 #9).

## 2.7 `GET` / `PUT /api/v1/settings`

Covers rows 18–25.

```json
{
  "port": 0,
  "cacheDir": "",
  "rescanDelaySeconds": 5,
  "rescanIntervalMinutes": 30,
  "logLevel": "Error",
  "startMinimized": false,
  "preventSleep": false,
  "autostart": true,
  "effective": { "port": 49312, "cacheDir": "C:\\Users\\ilya\\AppData\\Local\\SimpleDLNA" },
  "restartRequired": []
}
```

`effective` shows what is actually in force, which is the only way to explain
`port: 0` meaning "port 49312 today". After a `PUT` that changes `port` or
`cacheDir`, `restartRequired` lists them and the response is still `200` — the
value is stored, it just is not live. This replaces the GUI's *"(Requires
restart)"* tooltips.

| Field | Range | Applied |
| --- | --- | --- |
| `port` | 0–65535 | **restart** — the listener is `readonly` (`HTTPServer.cs:25,63`) |
| `cacheDir` | path or `""` | **restart**, and see §2.13 #3 |
| `rescanDelaySeconds` | 1–3600 | on next server start |
| `rescanIntervalMinutes` | 0–1440 (0 = off) | on next server start |
| `logLevel` | one of `capabilities.logLevels` | **immediately** — re-runs `SetupLogging` |
| `startMinimized` | bool | next launch; tray host only |
| `preventSleep` | bool | immediately — re-evaluates `SleepInhibitor.Inhibit` |
| `autostart` | bool | immediately — writes `HKCU\…\Run` value `SimpleDLNA` |

Ranges are the `NumericUpDown` limits from §1.4, enforced server-side as `422`.
`autostart` and `startMinimized` are absent from the payload when
`status.host == "console"`.

`PUT` replaces the whole object; `logLevel` outside the list is `422` rather than
the GUI's silent fallback (`FormSettings.cs:25-27`).

## 2.8 Maintenance

| Verb | Path | Behaviour | Checklist |
| --- | --- | --- | --- |
| `POST` | `/cache/drop` | `202`. Stops every running server, deletes `sdlna.cache`, restarts them — `FormMain.cs:273-303` minus the confirmation box, which becomes the SPA's job | 29 |
| `GET` | `/log?tail=200&level=Warn` | `200` — the last N parsed lines | 28 |

`GET /log` replaces *"Open Log Folder"*, which cannot work from a browser.

```json
{
  "path": "C:\\…\\sdlna.log", "level": "Error", "totalBytes": 184320,
  "lines": [
    { "timestamp": "2026-08-11T09:10:02Z", "level": "INFO",
      "logger": "PlaybackMonitor", "message": "Playback started: Blade Runner (192.168.1.44)" }
  ]
}
```

`tail` defaults to 200, caps at 5000. The file must be opened
`FileShare.ReadWrite` — log4net holds it open with `ImmediateFlush`
(`FormMain.cs:611-625`). Lines that do not match the pattern
(`%date %6level [%3thread] %-30.30logger{1} - %message`, `FormMain.cs:608`) are
returned with `level: null` and the raw text, so stack traces survive. When
`logLevel` is `None` there is no file: return `200` with an empty `lines` array
and `"disabled": true`.

## 2.9 `GET /api/v1/fs?path=`

The replacement for `FolderBrowserDialog` (rows 16 and 19). **Required, not
optional** — a browser cannot open a native folder picker.

```json
{
  "path": "D:\\Media",
  "parent": "D:\\",
  "entries": [
    { "name": "Movies", "path": "D:\\Media\\Movies", "hasChildren": true, "accessible": true }
  ]
}
```

With no `path`, returns the drive list (`DriveInfo.GetDrives()`, ready drives
only) with `parent: null`. Directories only — files are never listed, since the
picker only ever chooses directories. Unreadable subdirectories are returned with
`accessible: false` rather than omitted, so a permissions problem is visible
instead of looking like an empty folder. `404` for a path that does not exist,
`400` for a malformed one.

This endpoint enumerates the filesystem, which is why the whole API is
loopback-only (§2.11).

## 2.10 `GET /api/v1/events` — live state

Server-Sent Events. Feasible here precisely because the admin listener is ours:
the response writer simply omits `Content-Length` and streams, which the media
stack cannot do (§2.1 #7).

```
event: servers
data: {"id":"0f3d…","state":"running"}

event: playback
data: {"playing":false}

event: ping
data: {}
```

Three event types, all **nudges** rather than state transfer: `servers` (a
server's state changed — refetch it), `playback` (`PlaybackMonitor.Changed`,
`PlaybackMonitor.cs:84`), `ping` every 20 s to keep the connection alive and let
the client detect a dead process. The SPA treats any event as "invalidate and
refetch", so no ordering or replay guarantees are needed.

**Fallback:** if `EventSource` fails or the connection drops, the SPA polls
`GET /status` and `GET /servers` — every 1 s while any server is in a transitional
state (`loading`, `refreshing`), every 5 s otherwise. The API is fully usable with
`/events` ignored entirely, so this endpoint is a progressive enhancement and can
be deferred if implementation runs long.

## 2.11 Security model

The bind address **is** the security model, and it is worth being explicit that
this is the whole of it.

- `AdminServer` binds `IPAddress.Loopback:19199`. Not `Any`, not `IPv6Any`.
  Nothing outside the machine can reach the API, including hosts the DLNA server
  happily serves media to.
- No authentication, no tokens, no CSRF defence. Any local process, and any page
  in any local browser, can call it.
- **Consequences to accept knowingly:** a malicious local page can `POST` to
  `/api/v1/*`. Cross-origin *reads* are blocked by the browser's same-origin
  policy (no CORS headers are ever sent), but a simple-request `POST` still
  fires. Mitigations, cheap and worth doing:
  - Require `Content-Type: application/json` on all mutations — this makes them
    non-simple requests, so a cross-origin `POST` is preflighted, and the
    preflight fails because no CORS headers are returned.
  - Reject any request whose `Origin` header is present and is not
    `http://localhost:19199` / `http://127.0.0.1:19199`.
  - Reject requests whose `RemoteEndpoint` is not loopback, belt-and-braces
    against a future bind change.
- `GET /api/v1/fs` exposes directory names to anything that can reach the port.
  Loopback-only is what makes that acceptable.
- If the bind address is ever widened, a real auth story is a **prerequisite**,
  not a follow-up: at minimum a bearer token persisted in `settings.json` and
  shown in the tray menu. Out of scope for v1.

The existing `--ip` / `--mac` / `--ua` allowlist governs the **media** port only
(`HttpAuthorizer`, `Program.cs:108-118`) and has no bearing here.

## 2.12 Configuration storage

Today's split (§1.8) does not survive the GUI: `Properties.Settings` is
user-scoped `user.config` reachable only from a WinForms/desktop host, and
`sdlna.exe` has no equivalent.

**Move global settings to `settings.json`**, same schema as the `GET /settings`
payload minus `effective`/`restartRequired`, written atomically (temp + replace,
like `SaveConfig` at `FormMain.cs:527-546`).

Location: **the default cache directory** (`%LOCALAPPDATA%\SimpleDLNA`), never
the user-overridden one — the file holds `cacheDir` itself, so storing it there
would be circular. See §2.13 #3.

Migration, once, on first run: if `settings.json` is absent and
`Properties.Settings` has values, copy them across, write `settings.json`, and
leave `user.config` alone as a rollback. `descriptors.xml` keeps its format,
location and `XmlSerializer` — only the `Id` element is added (§2.2.2).

Autostart stays in the registry (`StartUpUtilities.cs`); it is Windows state, not
app config.

## 2.13 Findings that require changes to existing code

Each of these is a real defect found while designing the API. None is caused by
the API; all affect it.

1. **Request bodies are ASCII-lossy — this is a live bug, not just an API
   concern.** `HttpClient.cs:259` decodes the buffer with a `StreamReader` (UTF-8)
   and then re-encodes it with `Encoding.ASCII.GetBytes`, so every non-ASCII
   character in a request body becomes `?` before `Body` is read back as UTF-8
   (`:284`). Only the first segment is affected — later reads append raw bytes
   (`:232`) — which for small bodies means always. **This already corrupts SOAP
   `ContentDirectory` requests carrying non-ASCII search criteria.** The admin API
   sidesteps it by not using this parser, but the fix (buffer raw bytes, decode
   once at the end) belongs in `server/` on its own merits.
2. **`descriptors.xml` lives in the overridable cache directory.** `CacheDir`
   resolves through the `cache` setting (`FormMain.cs:131-137`) and
   `descriptors.xml` is written there (`:534-539`), so changing the cache
   directory makes every configured server disappear. The API must either keep
   `descriptors.xml` next to `settings.json` in the *default* directory, or
   migrate it on change. **Recommend the former** — configuration is not cache.
3. **`cache` is used as both a directory and a file path** (§1.11 #4). The API
   exposes it as `cacheDir` (a directory) and derives the media cache as
   `{cacheDir}/sdlna.cache`, dropping the file-path interpretation. Existing
   values that point at a file need one-time normalisation to their parent
   directory during the §2.12 migration.
4. **`LoadConfig` can NRE** on `config.Descriptors.Clear()` (§1.11 #5). Moot once
   `ServerManager` owns loading, but the legacy-migration path must not
   reintroduce it.

## 2.14 Checklist coverage

| Checklist rows (§1.12) | Covered by |
| --- | --- |
| 1, 2 | `GET /servers`, `GET /events` |
| 3, 4, 5 | `POST` / `PUT` / `DELETE /servers` |
| 6, 7, 8, 9 | `/servers/{id}/start\|stop\|rescan`, `/servers/rescan-all` |
| 10–17 | The server resource + validation (§2.6.1, §2.6.2) |
| 18–24 | `GET` / `PUT /settings` |
| 25 | `PUT /settings` → `preventSleep` |
| 26 | `GET /status` → `playback`, `GET /events` |
| 27 | `GET /status` → `browseUrl` |
| 28 | `GET /log` |
| 29 | `POST /cache/drop` |
| 30 | `GET /status` → `version`; licence text ships in the SPA (§3) |
| 31 | Not an API concern — a link in the SPA |
| 32, 33 | **Out of scope.** Tray and single-instance behaviour stay native (§4) |
| 34 | `GET /status` → `mediaPort` |

Rows 16 and 19 additionally require `GET /fs` (§2.9).

## 2.15 Deferred

- **Authentication** — see §2.11. Blocked on the bind address widening.
- **`PATCH` semantics** — no demand while the editor submits whole objects.
- **Per-server logs** — the log is process-wide; splitting it is a `server/`
  change.
- **`view` parameter discovery** — §2.5 option (b) is recommended but is a
  `server/` change; option (a) unblocks the SPA either way.
- **Restart-in-place for port / cache directory** — would need `HttpServer` to be
  disposable and re-creatable with every mount re-registered. Real work, and the
  GUI never did it either. `restartRequired` is honest in the meantime.

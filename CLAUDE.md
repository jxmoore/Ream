# CLAUDE.md

Guidance for working in this repository.

## What this is

Ream — a Windows desktop note-taking app (C# / WPF / .NET 8) with a
niri-style infinite layout: workspaces stack vertically (Alt+scroll to switch),
notes sit in a horizontally scrolling row per workspace, new notes open to the
right of the focused one. Notes are rich text (FlowDocument) with inline pasted
images. The earlier React/TypeScript/RxDB prototype was deleted; it exists only
in git history and should not be used as a reference.

The design and staged build plan (M0–M5) were agreed with the user; the plan
file is `C:\Users\JoeMo\.claude\plans\playful-crafting-taco.md`.

## Commands

- `dotnet build Ream.sln`
- `dotnet run --project src/Ream.App`
- `dotnet test`

## Architecture

- `src/Ream.Core` — WPF-free domain: models, repository interfaces, utilities.
- `src/Ream.Persistence` — file/JSON repositories, `AtomicFileWriter`, the
  `.reamnote` serializer (uses WPF `FlowDocument`, hence `UseWPF`).
- `src/Ream.App` — WPF exe. MVVM with CommunityToolkit.Mvvm; Generic Host for DI
  (`App.xaml.cs`, no `StartupUri`).
- `src/Ream.Tests` — xUnit; targets `net8.0-windows` because it exercises
  FlowDocument code.

Layout is two custom `Panel`s with manually animated offsets (a vertical
workspace strip, a horizontal note row per workspace), not `ScrollViewer`
virtualization: `Ream.App/Controls/WorkspaceStripPanel.cs` and `NoteRowPanel.cs`.
The row geometry (column widths, minimal-scroll vs centered offset) is pure math in
`Ream.Core/Layout/RowLayout.cs` and is unit-tested; keep it there, not in the panel.
Animation goes through `Ream.App/Animation/Motion.cs` (respects `AppConfig.Animations`).
Empty workspaces are pruned only after a switch animation settles
(`AppViewModel.PruneEmptyWorkspacesCommand`), using `SuppressAnimation` so the strip
snaps rather than animates when the list shifts under it.

Status: the M0-M5 plan is complete (layout engine, on-disk persistence, config.json, rich-text
editor with inline images, freeform column resize, workspace strip + rename, tilt-wheel,
config live-reload, themes, lazy note loading, crash recovery, portable packaging). A second
round followed (centered focus, edge workspaces + draft notes, size keys, app fullscreen, editable
note titles, 7 themes + settings panel + canvas opacity/blur + title bar, Word-style ribbon, File
menu with Help/About); see below. Anything further is new work; ask what the user wants next.

Themes: `Ream.Core/Models/ThemeCatalog.cs` lists the ids (dark = default, light, dracula, catppuccin,
material, nord, gruvbox; unknown ids and the retired "system" fall back to dark). Every
`Themes/<Id>.xaml` defines the same brush keys and stays readable (tests enforce both, incl.
contrast ratios). `Themes/Controls.xaml` holds the themed Button/ToggleButton/ComboBox/TextBox/
ScrollBar/Slider/ContextMenu/MenuItem styles. Always reference brushes with `{DynamicResource ...}`,
never StaticResource, or a theme switch won't reach them. `ThemeService` swaps
`Application.Resources.MergedDictionaries[0]` and publishes `CanvasBrush` (window background; alpha
from `canvasOpacity`, only where the system backdrop exists and `canvasBlur` is on) and
`FocusBorderBrush` (`layout.focusBorderColor`, else the theme accent). `ChromePlanner` /
`WindowBackdrop` (behind `IWindowBackdrop`) set the title bar, border and blur through DWM by
Windows build; the planner is unit-tested, the real DWM calls are not visible off-screen.
Settings panel: `SettingsViewModel` applies theme/opacity live and saves them (debounced) via
`AppConfigStore.Update`, which patches only the given keys and refuses to rewrite a file with
comments; `ConfigReloader` ignores the file-change echo of that save (`IsOurOwnLastWrite`).

Live reload: `ConfigReloader` re-reads config.json (via `ConfigWatcher`, debounced) and replaces
`AppViewModel.Config`; panels/bindings and the window's key bindings follow. It uses the
non-destructive `AppConfigStore.TryLoad`: an unusable file is reported in the toolbar
(`ConfigError`) and the running settings stay - never move or rewrite a file the user is
mid-edit. Only `documentsRoot` still needs a restart.

Lazy loading: `NoteRowPanel` marks each column `IsNear` (within a viewport of the screen, in a
workspace within ~1.5 screens); `NoteColumnView` (an `INearAware`) only parses its note in
`EnsureLoaded()` once near, focused, or pasted into, and never unloads. A view that is
constructed but never shown must be loaded explicitly (`EnsureLoaded()`) in tests.

Crash recovery: on load, leftover `*.tmp` files are promoted if complete and their real file is
missing, otherwise moved to `ReemDocuments/.recovered/<stamp>/` (never deleted).

Packaging: `build/publish.ps1` makes a portable single-file build + zip under `artifacts/`
(gitignored); `-FrameworkDependent` for the small one. It is not an installer.

Widths: a column's width is a plain fraction of the row (`NoteViewModel.WidthFraction`,
clamped to 0.15-1.0). Presets (1/3, 1/2, 2/3, full) are only labels/cycle stops
(`WidthPresets`); Alt+R goes to the next preset wider than the current width. Dragging a
column's right edge (`NoteColumnView.ResizeHandle`) sets the fraction live via
`RowLayout.FractionForWidth`; `NoteRowPanel.IsResizing` makes the panel follow the pointer
instead of animating. `layout.json` stores `widthFraction` (older files' `"width": "half"` still
load) and an optional `customTitle`. Workspaces: `WorkspaceTabs` chips (click = switch,
double-click or Shift+F2 = rename in place); the first and last chip are unnamed empty "+" edge
workspaces. Named workspaces are never pruned; unnamed empty ones between the edges are.
Draft notes (Alt+Left/Right past the end of a row, Alt+N): `NoteViewModel.IsDraft`, never stacked,
not saved while blank, dropped when focus moves on (`WorkspaceViewModel.DiscardBlankDrafts`);
the scroll wheels never create them.

Editor: each `NoteColumnView` hosts a `RichTextBox`; `NoteViewModel` owns the note's live
`FlowDocument` and only writes it into `Body` (the saved XML) in `FlushDocument()`, which
`SnapshotMapper.ToSnapshot` calls before every save. `RibbonView` (the Home tab of the Word-style
ribbon; the File button, tab row and menus live in `MainWindow.xaml`) acts on whichever
editor last had keyboard focus. Focus follows the app's note focus via
`AppViewModel.RequestEditorFocus` -> `NoteViewModel.EditorFocusRequested`.

Persistence flow: `PersistenceCoordinator` (Ream.App/Services) watches the view models,
debounces 300ms, snapshots on the UI thread via `SnapshotMapper`, then saves on a
background queue. `IDocumentRepository.Save` reconciles disk with the snapshot
(creates/moves/trashes note files, rewrites only changed layout/metadata).
The two always-empty edge workspaces (above the first, below the last) are never stored: a
workspace is persisted only if it is named or has a saveable note (blank drafts are not).
`Flush()` runs on app exit.

## Storage

- `ReemDocuments/metadata.json` — workspace order, names, current workspace.
- `ReemDocuments/ws-<guid8>/layout.json` + `<noteId:N>.reamnote` files, images in
  `assets/<noteId>/<guid>.png` (written at paste time via `IAssetStore`; they move/trash with
  their note; unreferenced images are never garbage-collected). Folder names are never
  derived from display names.
- `.reamnote` is a small whitelisted XML format (`ReamNote > Doc > P/UL/OL > R/BR/IMG`),
  NOT XAML: XamlReader can instantiate arbitrary types, so it is never used on note files.
  `NoteDocumentSerializer` writes only what differs from the paragraph/document baseline,
  refuses DTDs, ignores unknown elements, and rejects unsafe asset names. Text that isn't
  in this format (older notes) opens as plain paragraphs.
- Default documents folder is `%USERPROFILE%\Documents\ReemDocuments`; it is written
  into config.json as `documentsRoot` on first run and can be changed there.
- Closing a note (Alt+Q) never deletes it: the file moves to `ReemDocuments/.trash/<ws-folder>/`.
- `%AppData%\Ream\config.json` — gaps, centered focus, animations, keybindings.
  Kept separate from documents. Unreadable JSON is set aside as `*.corrupt-<timestamp>`
  and defaults are used; bad/duplicate keybindings fall back to defaults.
- `--home <dir>` on the command line puts config and documents under one folder. Use it
  (with a temp dir) when running the app for testing so real data is never touched.
- All writes go through `AtomicFile` (write `.tmp`, flush, replace). On load, damaged
  layout/metadata files are quarantined and rebuilt from the note files and `ws-*` folders,
  and note files missing from a layout are adopted, so nothing on disk is orphaned.
- Note/folder names read from JSON are validated (no path separators) before use.

## Conventions and gotchas

- Plain mouse wheel is never intercepted at the shell level — it scrolls the
  note under the cursor. Alt+wheel switches workspaces, Shift+wheel pans the row.
- Keybindings are read from config: one gesture per action, defaults in
  `AppConfig.DefaultKeybindings`. Every action also needs a description in `ActionCatalog` (a test
  enforces it) so the Help window lists it. F11 = app fullscreen (`FullscreenController` behind
  `IWindowFrame`), Shift+F11 = the focused note's fullscreen.
- `RichTextBox.Document` is not a DependencyProperty — the editor owns its
  document in code-behind; don't try to bind it.
- `XamlWriter`/`XamlReader` don't round-trip images; `NoteDocumentSerializer`
  handles images as sibling asset files with an `asset://` placeholder.
- Soft scope: no import/export, sync, or auth in v1.
- The WPF SDK removes `System.IO` from implicit usings; `Ream.Persistence` and
  `Ream.Tests` add it explicitly via `<Using Include="System.IO" />`.
- `JsonDefaults` uses relaxed escaping so hand-edited files read `Alt+Right`, not
  `Alt+Right`. Keep it that way for anything a user might edit.
- `dotnet test` may leave `Ream.App/bin` stale; run `dotnet build Ream.sln` before
  launching the app to test changes.
- A `FlowDocument` does NOT inherit font/color from its `RichTextBox`; `NoteColumnView` copies
  the editor's defaults onto the document. `TextRange.ApplyPropertyValue` rejects
  `DependencyProperty.UnsetValue` (throws) - apply explicit defaults or null instead.
- A `TextPointer` scan reports an `InlineUIContainer` at both its start and end; dedupe.
- `TextRange` can't clear a property (`UnsetValue` throws). To return text to "automatic" color,
  apply a marker brush (which splits runs at the selection edge) then `ClearValue` on the
  elements holding the marker (`RibbonView.ClearAutomaticColor`). Never copy today's theme
  brush into the document as a local value.
- Don't name a field `_contentLoaded` in a XAML code-behind: the XAML compiler generates one.
- A UserControl's own implicit `Button`/`ComboBox` style replaces the app-wide themed one unless it
  has `BasedOn="{StaticResource {x:Type Button}}"` (see `RibbonView.xaml`).
- `Ui.StartHost` sets the `Ream.SkipStartup` AppContext switch so constructing `App` never runs the
  real `OnStartup` (real config, real documents, a real window). Keep it. The workspace list always
  has an empty edge workspace at both ends, so `Workspaces[0]` is not the first real workspace.
- Known open issue: the full suite hangs intermittently (roughly 1 run in 10, early on, with many
  unrelated tests in flight); not yet diagnosed. `Ui.Run` has timeouts; run with
  `--blame-hang --blame-hang-timeout 90s`.
- Tests that change the theme must restore dark (`Themes.Use` in the tests does), since the
  `Application` is shared by every UI test.
- `Ui.Run` serializes UI tests with a lock: `Settle()` pumps the shared UI thread, so without
  it another test's queued work runs nested inside the current one and steals process-wide
  keyboard focus (this caused a real flaky failure). Keep every test that touches WPF views
  behind `Ui.Run`. Horizontal-wheel is tested by sending `WM_MOUSEHWHEEL` to the test's own
  off-screen window, never to the real desktop.
- Test editor behaviour in-process (`Ream.Tests/Ui.cs` hosts real views on a UI thread,
  off-screen, with the app's resources; raise `Click` on toolbar buttons, use
  `Editor.AppendText`, `RenderToPng` to look at output). Do NOT drive the user's live
  desktop with SendKeys/mouse events: other input lands in the same window and keys can
  reach the wrong app. If you must run the real app, use `--home <temp dir>`, capture with
  `PrintWindow` only, and never delete a path built from a variable without validating it
  (PowerShell's `$Home` is read-only and silently resolves to the user profile).

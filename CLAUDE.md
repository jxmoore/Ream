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

## Architecture

Layout is two custom `Panel`s with manually animated offsets (a vertical
workspace strip, a horizontal note row per workspace), not `ScrollViewer`
virtualization: `Ream.App/Controls/WorkspaceStripPanel.cs` and `NoteRowPanel.cs`.
The row geometry (column widths, minimal-scroll vs centered offset) is pure math in
`Ream.Core/Layout/RowLayout.cs` and is unit-tested; keep it there, not in the panel.
Animation goes through `Ream.App/Animation/Motion.cs` (respects `AppConfig.Animations`).
Empty workspaces are pruned only after a switch animation settles
(`AppViewModel.PruneEmptyWorkspacesCommand`), using `SuppressAnimation` so the strip
snaps rather than animates when the list shifts under it.

Themes: `Ream.Core/Models/ThemeCatalog.cs` lists the ids (dark = default, light, dracula, catppuccin,
material, nord, gruvbox; unknown ids and the retired "system" fall back to dark). Every
`Themes/<Id>.xaml` defines the same brush keys and stays readable (tests enforce both, incl.
contrast ratios). `Themes/Controls.xaml` holds the themed Button/ToggleButton/ComboBox/TextBox/
ScrollBar/Slider/ContextMenu/MenuItem styles. Always reference brushes with `{DynamicResource ...}`,
never StaticResource, or a theme switch won't reach them. `ThemeService` swaps
`Application.Resources.MergedDictionaries[0]` and publishes `CanvasBrush` (alpha from `canvasOpacity`,
min 1 so a 0% window still catches clicks), `RibbonBrush` and `WindowBorderBrush` (the ribbon and the
1 px window outline, both exactly the canvas alpha, so at 0% nothing of Ream is behind the notes),
`NoteBrush` (each note's card, alpha from `noteOpacity`, may be 0), `ChromeHoverBrush` / `RibbonHoverBrush` (the canvas / toolbar
color at max(canvas alpha, up to 85% as the canvas fades to clear)). Ream's own bare text (tab labels, title, group labels, workspace
label) floats unbacked at low opacity; the title bar, tab row, ribbon panel and workspace label swap their clear `Background` for
the hover brush in an `IsMouseOver` style trigger (so set the base Background in the style, not as a local attribute, or the trigger
loses), which keeps light text readable over a bright desktop exactly when you reach for it. And
`FocusBorderBrush` (`layout.focusBorderColor`, else the theme accent).
The window frame is drawn by Ream (`MainWindow.xaml`: `WindowStyle=None`, `AllowsTransparency`,
`WindowChrome`, own title bar and min/max/close). The window itself is transparent and each region
(`TitleBar`, `TabRow`, `RibbonPanel`, `CanvasArea`) paints its own brush once, so translucent brushes
never stack; tests read the rendered pixels (`Ui.Render`/`Ui.PixelAt`) to check color and alpha.
Blur behind is separate (`canvasBlur`, and never asked for at 0% - a blurred desktop is something of Ream): `WindowBackdrop` (behind `IWindowBackdrop`) asks Windows for it via
`SetWindowCompositionAttribute`; `BlurPlan` is unit-tested, the real effect is not visible off-screen.
Help/About are still ordinary native windows.
Ribbon: the tab row is File | Home | View (`MainWindow.SelectTab`, `RibbonTab`); each tab swaps the panel below it:
`FileRibbonView` (New / Open / Save / Save As, the Auto-save switch, Clear, and Help/About raised as events; bound to the `AppViewModel` ream commands, tooltips show the live gestures; there is no workspace list - workspaces are switched by keys and the wheel), `RibbonView` (Home, the
editor controls) and `ViewRibbonView` (a theme dropdown bound to `SettingsViewModel.SelectedTheme`, canvas and note opacity sliders stacked; DataContext is the
`SettingsViewModel`). Home is laid out like Word's Home tab (flat buttons, icon rows, a label under each group with its launcher corner): Clipboard, Font, Paragraph, Styles
(a framed gallery), Editing, Add-ins, then Ream's own Size group (the three reset commands, stacked). Word controls Ream has no behaviour for yet (Format Painter,
change case, sub/superscript, multilevel list, sort, ¶, line spacing, shading, borders, Find/Replace/Select, Add-ins) are drawn but disabled
(`RibbonWordLayoutTests.Placed` lists them; enabling one means adding its handler and removing `IsEnabled="False"`) that sits outside `RibbonView.Bar` so it works with no editor focused. When the window is too narrow
the panel scrolls sideways with no scrollbar: chevron buttons (`RibbonScrollLeft/Right`) appear at the edge with more to see, and the wheel scrolls. There is no File menu or
settings popup any more. `ribbon.autoHide` (default true): the tab row stays, the panel (always grid row 2) grows from height 0 when
summoned and back to 0 when put away, so it pushes the notes down rather than covering them (`Core/Layout/RibbonVisibility` is the
pure state: pointer, open menu, pin; the window adds a 400 ms hide delay). Clicking a tab holds it open (`Engaged`) until a click
elsewhere or Escape (`MainWindow.DismissRibbon`); the pin button (`PinButton`, bottom right of the panel) keeps it open until
clicked again; like Word's it is a pin while unpinned and a caret up once pinned. autoHide off leaves the panel up always (and hides the pin).
Watch `ComboBox.IsDropDownOpen` itself, not DropDownOpened/Closed (Closed can arrive before the property flips).
`SettingsViewModel` applies theme and both opacities live and saves them (debounced) via
`AppConfigStore.Update`, which patches only the given keys and refuses to rewrite a file with
comments; `ConfigReloader` ignores the file-change echo of that save (`IsOurOwnLastWrite`).

Live reload: `ConfigReloader` re-reads config.json (via `ConfigWatcher`, debounced) and replaces
`AppViewModel.Config`; panels/bindings and the window's key bindings follow. It uses the
non-destructive `AppConfigStore.TryLoad`: an unusable file is reported in the toolbar
(`ConfigError`) and the running settings stay - never move or rewrite a file the user is
mid-edit.

Lazy loading: `NoteRowPanel` marks each column `IsNear` (within a viewport of the screen, in a
workspace within ~1.5 screens); `NoteColumnView` (an `INearAware`) only parses its note in
`EnsureLoaded()` once near, focused, or pasted into, and never unloads. A view that is
constructed but never shown must be loaded explicitly (`EnsureLoaded()`) in tests.

Crash recovery: on load, leftover `*.tmp` files are promoted if complete and their real file is
missing, otherwise moved to the ream's `.recovered/<stamp>/` folder (never deleted).

Packaging: `build/publish.ps1` makes a portable single-file build + zip under `artifacts/`
(gitignored); `-FrameworkDependent` for the small one; `-Version x.y.z` stamps a version. It is not an installer.
Releases: a push to `main` runs only the `release` job in `.github/workflows/tests.yml` (the tests run on every branch push, and a ruleset requires them to pass on a PR's source branch, so main does not re-run them): GitVersion (`GitVersion.yml`)
works out a plain major.minor.patch (the `MajorMinorPatch` variable only - never `FullSemVer`, which grows a `-N` suffix), the
framework-dependent zip is published with that version, and a GitHub release `v<version>` is created (which also tags it). Each merge to
main is a patch bump; `+semver: minor` / `+semver: major` in a commit message bumps more. A commit that already has a release is skipped.

Widths: a column's width is a plain fraction of the row (`NoteViewModel.WidthFraction`,
clamped to 0.15-1.0). Presets (1/3, 1/2, 2/3, full) are only labels/cycle stops
(`WidthPresets`); Alt+R goes to the next preset wider than the current width. Dragging a
column's right edge (`NoteColumnView.ResizeHandle`) sets the fraction live via
`RowLayout.FractionForWidth`; `NoteRowPanel.IsResizing` makes the panel follow the pointer
instead of animating. `layout.json` stores `widthFraction` (older files' `"width": "half"` still
load) and an optional `customTitle`. Workspaces have no chips any more: the title bar says just "Ream"
(room for a ream name later) and the workspace's name is a label in the bottom-right corner
(`AppViewModel.WorkspaceLabel`; unnamed = "Workspace N", the empty edges = "New workspace"); double-click it or press
Shift+F2 to rename in place, and the File ribbon lists them for the mouse. The first and last workspace are
unnamed empty edges. Named workspaces are never pruned; unnamed empty ones between the edges are.
Switching workspace (keys, Alt+wheel, menu) lands on the first note when `layout.focusFirstNoteOnSwitch`
(default true); moving a note keeps it focused. `NoteRowPanel.IsCurrentWorkspace` makes a row snap rather
than scroll while its workspace is off-screen. Note width is not shown on the note: `NoteViewModel.ShowSizeToast`
flashes "NN%" in the header (accent color) after Alt+=/-/R, resets (Alt+Shift+R note, Ctrl+Alt+R workspace, Ctrl+Alt+Shift+R all; Alt+0-9 are reserved for jumping to workspaces) and edge drags.
Default `layout.gapPx` is 28. Draft notes (Alt+Left/Right past the end of a row, Alt+N): `NoteViewModel.IsDraft`, never stacked,
not saved while blank, dropped when focus moves on (`WorkspaceViewModel.DiscardBlankDrafts`);
the scroll wheels never create them.

Editor: each `NoteColumnView` hosts a `RichTextBox`; `NoteViewModel` owns the note's live
`FlowDocument` and only writes it into `Body` (the saved XML) in `FlushDocument()`, which
`SnapshotMapper.ToSnapshot` calls before every save. `RibbonView` (the Home tab of the Word-style
ribbon; the File button, tab row and menus live in `MainWindow.xaml`) acts on whichever
editor last had keyboard focus. Focus follows the app's note focus via
`AppViewModel.RequestEditorFocus` -> `NoteViewModel.EditorFocusRequested`.

Reams (files): a *ream* is a `Foo.ream` file plus a data folder (formats: `src/Ream.Persistence/CLAUDE.md`). One is open at a
time, in one window; opening another swaps the content of the existing `AppViewModel` in place (`AppViewModel.LoadReam`,
`SnapshotMapper.LoadInto`), so the window, settings and key bindings stay. The pieces (all `Ream.App/Services`):
- `ReamLauncher` picks the ream at launch: `config.lastReam`; else an old `ReemDocuments` folder converted in place
  (`LegacyConverter`, backup copy first) or the `.ream` already in it; else a new ream (`Documents\Ream\My Ream.ream`, with the
  tutorial, or empty if `tutorialOnNew` is false). A last ream that is missing or unreadable is explained, never fatal.
- `ReamSession` = one open ream: its `DocumentRepository` plus a `PersistenceCoordinator`, and it tells the view model the ream's
  name, path and unsaved state (`WindowTitle` is `Ream - Foo`, with ` *` while unsaved and auto-save is off).
- `PersistenceCoordinator` watches the view models; after a 300 ms quiet period it snapshots on the UI thread (`SnapshotMapper`) and
  compares a content fingerprint (`SnapshotFingerprint`) with the last saved one: that is `HasUnsavedChanges`. Moving focus, switching
  workspace, the empty edge workspaces and blank drafts are not edits. With auto-save on the snapshot is written on a background
  queue (`IDocumentRepository.Save` reconciles disk: creates/moves/trashes note files, rewrites only what changed); with it off nothing
  is written until `Save()`. `Flush()` (app exit) writes only when auto-save is on.
- `ReamManager` is New / Open / Save / Save As / Clear / auto-save switch / "may I leave this ream?" (`ConfirmLeave`, used by New, Open
  and closing the window: with auto-save off and unsaved changes it asks Save / Don't save / Cancel). Every question goes through
  `IFileDialogs` and `IUserPrompts` (real ones are Ream's own themed windows; tests use `FakeDialogs` / `FakePrompts`), and
  `AppViewModel` reaches it through `IReamFiles` (commands `NewReam`, `OpenReam`, `SaveReam`, `SaveReamAs`, `ClearReam`,
  `ToggleAutoSave`; key ids `newReam`, `openReam`, `save`, `saveAs`, `clearReam`). New and Save As never overwrite (`ReamPaths.IsOccupied`);
  Save As (`ReamCopier`) copies the ream, brings the copy up to date with memory, and switches to it; Clear (Alt+Shift+Q) asks first and
  swaps in an empty ream - saving that trashes what disappeared, so nothing is deleted.
- Help window (`HelpWindow`, `HelpNavigator`): the switch-workspace keys (from config, Alt+Down/Up by default) move an accent border through the
  sections, centered, and past the last one onto the Close button. Never `Keyboard.ClearFocus()` there: with nothing focused the
  navigation keys stop reaching the window (keyboard focus goes to the Close button or the content root). Dialogs (Help, About) use
  `Style="{StaticResource ModalWindowStyle}"` (Themes/Controls.xaml): the same drawn title bar as the main window
  (`CaptionButtonStyle` is shared too) instead of a native caption; new dialogs should use it. Questions and errors are `PromptWindow` (behind `IUserPrompts`); Open / New / Save As are `FileBrowserWindow` (behind `IFileDialogs`, as `ThemedFileDialogs`), whose rules live in the UI-free `FileBrowserModel` over `IFileSystem` (in-memory `FakeFileSystem` in tests). Only the startup-failure MessageBox in `App.xaml.cs` stays native.
- `TutorialReam` (Ream.Core) builds the tutorial (3 workspaces, 8 notes) from the live keybindings and settings, or an empty ream.
The two always-empty edge workspaces (above the first, below the last) are never stored: a workspace is persisted only if it is
named or has a saveable note (blank drafts are not); a ream with no workspaces is a valid, cleared ream (not a first run).

## Storage

- Reams live wherever the user puts them (New / Save As pick the place). With no last ream, a fresh one is made at
  `%USERPROFILE%\Documents\Ream\My Ream.ream`. `config.json` remembers `lastReam`; `documentsRoot` is legacy, read once to find an
  old folder to convert.
- `%AppData%\Ream\config.json` — gaps, centered focus, animations, keybindings, autoSave, tutorialOnNew, lastReam.
  Kept separate from the reams. Unreadable JSON is set aside as `*.corrupt-<timestamp>`
  and defaults are used; bad/duplicate keybindings fall back to defaults.
- `--home <dir>` on the command line puts config and reams (`<dir>\Reams`) under one folder. Use it
  (with a temp dir) when running the app for testing so real data is never touched.
- File formats (`.ream`, `.reamlayout`, `.reamnote`, data folder, trash, atomic writes, recovery, conversion) are described in
  `src/Ream.Persistence/CLAUDE.md`, which loads when you work there.

## Conventions and gotchas

- Plain mouse wheel is never intercepted at the shell level — it scrolls the
  note under the cursor. Alt+wheel switches workspaces, Shift+wheel pans the row.
- Keybindings are read from config: one gesture per action, defaults in
  `AppConfig.DefaultKeybindings`. Every action also needs a description in `ActionCatalog` (a test
  enforces it) so the Help window lists it. F11 = app fullscreen (`FullscreenController` behind
  `IWindowFrame`), Alt+F11 = the focused note's fullscreen.
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
- Test in-process, never on the user's live desktop. Do NOT drive it with SendKeys/mouse events:
  other input lands in the same window and keys can reach the wrong app. If you must run the
  real app, use `--home <temp dir>`, capture with `PrintWindow` only, and never delete a path
  built from a variable without validating it (PowerShell's `$Home` is read-only and silently
  resolves to the user profile). Test-harness details (`Ui.Run`, `Ream.SkipStartup`, timing) are in
  `src/Ream.Tests/CLAUDE.md`, which loads when you work there.

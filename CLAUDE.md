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
`FileRibbonView` (Open, disabled; Help/About raised as events; there is no workspace list - workspaces are switched by keys and the wheel), `RibbonView` (Home, the
editor controls) and `ViewRibbonView` (a theme dropdown bound to `SettingsViewModel.SelectedTheme`, canvas and note opacity sliders stacked; DataContext is the
`SettingsViewModel`). Home's busy groups are two rows deep (Font, Paragraph, Cut/Copy beside Paste), plus a Size group (the three reset
commands, stacked) that sits outside `RibbonView.Bar` so it works with no editor focused. When the window is too narrow
the panel scrolls sideways with no scrollbar: chevron buttons (`RibbonScrollLeft/Right`) appear at the edge with more to see, and the wheel scrolls. There is no File menu or
settings popup any more. `ribbon.autoHide` (default true): the tab row stays, the panel (always grid row 2) grows from height 0 when
summoned and back to 0 when put away, so it pushes the notes down rather than covering them (`Core/Layout/RibbonVisibility` is the
pure state: pointer, open menu, pin; the window adds a 400 ms hide delay). Clicking a tab holds it open (`Engaged`) until a click
elsewhere or Escape (`MainWindow.DismissRibbon`); the pin button (`PinButton`, inside the panel's right edge) keeps it open until
clicked again. autoHide off leaves the panel up always (and hides the pin).
Watch `ComboBox.IsDropDownOpen` itself, not DropDownOpened/Closed (Closed can arrive before the property flips).
`SettingsViewModel` applies theme and both opacities live and saves them (debounced) via
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

Persistence flow: `PersistenceCoordinator` (Ream.App/Services) watches the view models,
debounces 300ms, snapshots on the UI thread via `SnapshotMapper`, then saves on a
background queue. `IDocumentRepository.Save` reconciles disk with the snapshot
(creates/moves/trashes note files, rewrites only changed layout/metadata).
The two always-empty edge workspaces (above the first, below the last) are never stored: a
workspace is persisted only if it is named or has a saveable note (blank drafts are not).
`Flush()` runs on app exit.

## Storage

- Default documents folder is `%USERPROFILE%\Documents\ReemDocuments`; it is written
  into config.json as `documentsRoot` on first run and can be changed there.
- `%AppData%\Ream\config.json` — gaps, centered focus, animations, keybindings.
  Kept separate from documents. Unreadable JSON is set aside as `*.corrupt-<timestamp>`
  and defaults are used; bad/duplicate keybindings fall back to defaults.
- `--home <dir>` on the command line puts config and documents under one folder. Use it
  (with a temp dir) when running the app for testing so real data is never touched.
- File formats (`metadata.json`, `layout.json`, `.reamnote`, trash, atomic writes, recovery) are described in
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

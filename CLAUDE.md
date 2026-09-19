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

Status: M0-M4 done (layout engine, on-disk persistence, config.json, rich-text editor
with inline images, freeform column resize, workspace strip + rename, tilt-wheel). Next is
M5: polish (config live-reload, virtualization, theming, first-run polish, packaging).

Widths: a column's width is a plain fraction of the row (`NoteViewModel.WidthFraction`,
clamped to 0.15-1.0). Presets (1/3, 1/2, 2/3, full) are only labels/cycle stops
(`WidthPresets`); Alt+R goes to the next preset wider than the current width. Dragging a
column's right edge (`NoteColumnView.ResizeHandle`) sets the fraction live via
`RowLayout.FractionForWidth`; `NoteRowPanel.IsResizing` makes the panel follow the pointer
instead of animating. `layout.json` stores `widthFraction`; older files' `"width": "half"`
still load. Workspaces: `WorkspaceTabs` chips (click = switch, double-click or Alt+Shift+R =
rename in place). Named workspaces are never pruned or dropped when empty; unnamed empty ones are.

Editor: each `NoteColumnView` hosts a `RichTextBox`; `NoteViewModel` owns the note's live
`FlowDocument` and only writes it into `Body` (the saved XML) in `FlushDocument()`, which
`SnapshotMapper.ToSnapshot` calls before every save. `EditorToolbar` acts on whichever
editor last had keyboard focus. Focus follows the app's note focus via
`AppViewModel.RequestEditorFocus` -> `NoteViewModel.EditorFocusRequested`.

Persistence flow: `PersistenceCoordinator` (Ream.App/Services) watches the view models,
debounces 300ms, snapshots on the UI thread via `SnapshotMapper`, then saves on a
background queue. `IDocumentRepository.Save` reconciles disk with the snapshot
(creates/moves/trashes note files, rewrites only changed layout/metadata).
The always-empty trailing workspace is never stored. `Flush()` runs on app exit.

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
- Keybindings default to Alt+<key> but are read from config, not hardcoded
  (hardcoded to defaults only until M2).
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

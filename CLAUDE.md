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

Status: M1 (static layout + fake data in `Fake/SampleData.cs`) is done; real
persistence and config-file loading are M2.

## Storage

- `ReemDocuments/metadata.json` — workspace order, names, current workspace.
- `ReemDocuments/ws-<guid8>/layout.json` + `<noteId>.reamnote` files, images in
  `assets/<noteId>/`. Folder names are never derived from display names.
- `%AppData%\Ream\config.json` — gaps, centered focus, animations, keybindings.
  Kept separate from documents.
- All writes go through `AtomicFileWriter` (write `.tmp`, then replace).

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

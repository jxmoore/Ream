# Ream

[![Tests](https://github.com/jxmoore/Ream/actions/workflows/tests.yml/badge.svg?branch=develop)](https://github.com/jxmoore/Ream/actions/workflows/tests.yml?query=branch%3Adevelop)

A Windows desktop note-taking app built around an infinitely scrollable,
[niri](https://github.com/niri-wm/niri)-style layout.

> **Status: early development.** This repo was restarted from scratch as a
> C#/WPF app; the previous React/markdown prototype lives on only in git history.

## The idea

- **Workspaces** stack vertically. Switch between them with Alt+scroll or
  Alt+Up/Down. They're created on demand and pruned when empty.
- **Notes** sit side by side in a horizontally scrolling row within a workspace.
  A new note opens to the right of the focused one.
- **Columns resize** by dragging their right edge to any width, or with Alt+R to step
  through niri-style presets (1/3, 1/2, 2/3, full); any note can go fullscreen.
- **A workspace strip** in the toolbar: click to switch, double-click (or Alt+Shift+R) to
  name one. Named workspaces are kept even when empty.
- **Horizontal scroll** (a tilt wheel or Shift+scroll) moves along the row.
- **A real rich-text editor** — fonts, sizes, bold/italic/underline/strikethrough, colors and
  highlights, headings, lists, alignment, and images pasted straight in with Ctrl+V.
- **Configurable** via a niri-style `config.json` (gaps, centered focus,
  animation timing, keybindings, light/dark/system theme). Edits apply as soon as you save
  the file; if it can't be used, a warning appears in the toolbar and the previous settings stay.
- **Fast with many notes:** a note's text is only loaded once it is near the screen.
- **Safe on crashes:** interrupted writes are recovered or set aside, never lost.

A *ream* is your notes as plain files on disk: a `Foo.ream` file (the list of workspaces) with a `Foo`
folder beside it holding one folder per workspace, one file per note (`.reamnote`), a `.reamlayout` file describing
the arrangement, and any pasted images. Closing a note moves it to the ream's `.trash` folder rather than deleting it.
Use the **File** tab to start a new ream (with a short tutorial), open one, save, save a copy under a new name, or clear
the current one; auto-save can be switched off there or in `config.json`, in which case the title shows a `*` while there
are unsaved changes and Ream asks before you close. Ream reopens the ream you had open last.

Settings live in `%AppData%\Ream\config.json`, created on first run. Edit it to change
the gap between columns, centered focus, animation timing, auto-save, whether new reams start with the tutorial,
and every keyboard shortcut.

## Build and run

Requires the .NET 8 SDK on Windows.

```
dotnet build Ream.sln
dotnet run --project src/Ream.App
dotnet test
```

To make a portable copy you can hand to someone (no installer, no .NET needed):

```
.\build\publish.ps1                      # self-contained single exe + zip in artifacts\
.\build\publish.ps1 -FrameworkDependent  # ~2 MB, needs the .NET 8 Desktop Runtime
```

Every push to `main` also publishes a GitHub release: the framework-dependent zip, versioned `major.minor.patch`
by GitVersion (see `GitVersion.yml`; a `+semver: minor` or `+semver: major` in a commit message bumps more than the patch).

## Layout

- `src/Ream.Core` — domain models and abstractions (no WPF dependency)
- `src/Ream.Persistence` — file/JSON storage and the note serializer
- `src/Ream.App` — the WPF application
- `src/Ream.Tests` — xUnit tests

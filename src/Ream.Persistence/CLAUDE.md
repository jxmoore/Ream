# CLAUDE.md (src/Ream.Persistence)

Storage formats and rules. Loaded only when working under this directory; the project-wide rules are in the root
`CLAUDE.md` (config location, `--home`).

## A ream on disk

A *ream* is a named file plus a data folder (`ReamPaths` has the helpers; `DocumentRepository` reads and writes it):

```
<dir>\Foo.ream                       JSON: schemaVersion, dataFolder, workspaces[], currentWorkspaceId
<dir>\Foo\                           the data folder (named by "dataFolder" in the .ream; "." = the .ream's own folder)
    ws-<guid8>\layout.reamlayout     JSON: the notes in order, widths, titles, focused note
    ws-<guid8>\<noteId:N>.reamnote   the note (XML, below)
    ws-<guid8>\assets\<noteId>\<guid>.png   images (written at paste time via `IAssetStore`; they move/trash with their note;
                                     unreferenced images are never garbage-collected)
    .trash\  .recovered\
```

- The ream's **name is the file name**; nothing inside stores it. Renaming `Foo.ream` in Explorer is fine: the file records its
  data folder, so it still opens. `dataFolder` is validated like every name read from JSON (`"."` or one plain folder name).
- New reams get a data folder named after the file (`Foo`). `"."` is used by the in-place conversion of the old
  `ReemDocuments` folder (`LegacyConverter`: backup copy first, then rename `metadata.json` -> `<folder>.ream` and each
  `layout.json` -> `layout.reamlayout`) and by tests (`TestReam.Repo`).
- `DocumentRepository(reamFile, dataFolder = null)`: the file need not exist; an existing file's recorded data folder wins.
  A `.ream` with no workspaces (a cleared ream) is **not** a first run - only "no file and nothing in it" is.
- `Save` writes notes, then layouts, then the `.ream` last, so a crash in between leaves data the loader can adopt.
  Folder names are never derived from display names.
- `New` / `Save As` never overwrite: `ReamPaths.IsOccupied` (the file or its data folder exists). `ReamCopier` (Save As) copies
  only the `ws-*` folders, writes the `.ream` last and cleans up what it made on failure.
- `.reamnote` is a small whitelisted XML format (`ReamNote > Doc > P/UL/OL > R/BR/IMG`),
  NOT XAML: XamlReader can instantiate arbitrary types, so it is never used on note files.
  `NoteDocumentSerializer` writes only what differs from the paragraph/document baseline,
  refuses DTDs, ignores unknown elements, and rejects unsafe asset names. Text that isn't
  in this format (older notes) opens as plain paragraphs.
- Closing a note (Alt+Q) never deletes it: the file moves to `<data folder>/.trash/<ws-folder>/`. Clearing a ream works the same way
  (it just saves an empty ream, and the normal save trashes what disappeared).
- All writes go through `AtomicFile` (write `.tmp`, flush, replace). On load, leftover `*.tmp` files (including `Foo.ream.tmp`, which
  sits beside the .ream, outside the data folder) are promoted if complete and their real file is missing, otherwise moved to
  `.recovered/<stamp>/`. Damaged `.ream`/layout files are quarantined (`*.corrupt-<timestamp>`) and rebuilt from the `ws-*` folders and note
  files; note files missing from a layout are adopted, so nothing on disk is orphaned. A leftover `layout.json` is read if
  `layout.reamlayout` is missing, and removed on the next save.
- Note/folder names read from JSON are validated (no path separators) before use.

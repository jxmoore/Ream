# CLAUDE.md (src/Ream.Persistence)

Storage formats and rules. Loaded only when working under this directory; the project-wide rules are in the root
`CLAUDE.md` (default documents folder, config location, `--home`).

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
- Closing a note (Alt+Q) never deletes it: the file moves to `ReemDocuments/.trash/<ws-folder>/`.
- All writes go through `AtomicFile` (write `.tmp`, flush, replace). On load, damaged
  layout/metadata files are quarantined and rebuilt from the note files and `ws-*` folders,
  and note files missing from a layout are adopted, so nothing on disk is orphaned.
- Note/folder names read from JSON are validated (no path separators) before use.

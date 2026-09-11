# CLAUDE.md

Guidance for working in this repository.

## What this is

Ream — a local-first markdown note app. Pages are markdown cards laid out in a
**vertical column** (a document feed): each line is one page or a left-anchored
**row** of pages side by side. File import and PDF/markdown export included. This
repo implements the **free tier (Phases 0–4)** of
[`ream-implementation-guide.md`](./ream-implementation-guide.md). Sync, auth,
subscriptions, premium import, and the native shell are **not** built yet — keep
new work within the free, local scope unless asked.

> Note: the guide describes an *infinite pan/zoom canvas*; per later user
> direction the layout was replaced with the single vertical column described
> below. The data model is unchanged (pages + row-groups).

## Commands

- `npm run dev` — Vite dev server on :5173
- `npm run build` — `tsc -b` then `vite build`
- `npm run typecheck` — type-check only

## Architecture (data flows one direction)

```
RxDB (Dexie) ──reactive query──▶ useRxData ──▶ hooks ──▶ components
     ▲                                                       │
     └──────────────── services (writes) ◀───────────────────┘
```

- **Components never call RxDB directly.** All writes go through
  `src/services/*` (pageService, groupService, canvasService). All reads go
  through reactive hooks in `src/hooks/` built on `useRxData`.
- **Database**: `src/db/database.ts` is the singleton. Schemas in `schemas.ts`,
  TS types in `types.ts`, default-document factories in `factories.ts`, first-run
  seed in `seed.ts`, React context in `DbProvider.tsx`.
- **Layout = a vertical column of lines** (`canvas/Board.tsx`, orchestration
  only). **Every page belongs to a row** (`groups`); a 1-page row renders as a
  bare page, a 2+ page row as a left-anchored row inside a subtle, mostly-
  transparent box (header is minimal, shows on hover). Column order = `group.order`;
  within-row order = `page.order`. `services/columnService.ts` owns ALL structural
  edits (`appendPageLine`, `addPageToRow`, `movePageIntoRow`, `movePageToLine`,
  `moveRowToLine`, `ungroupRow`, `removePage`, `normalizeColumn`, `appendRow`).
- **Drag** (`useColumnDrag.ts` + `columnDrop.ts`): drag a page over another's
  **center** → reorder onto its own line (horizontal `column__bar`); over a page's
  **side/gap** → merge into that row (vertical `row__slot`). Drag a multi-page row
  by its header grip → reorder the whole row. Content-width rows + left alignment
  keep every line's first page on one left edge (adding to a row extends rightward
  without moving the first page).
- **Rendering**: `ColumnRow` (line), `PageCard` (thumbnail), `TopBar`, `DragLayer`
  (ghost), `RowMenu`/`CardMenu` popovers.
- **Every page is one uniform size**: `--page-width` + `--paper-ratio` (8.5×11)
  in `global.css`, `PAGE_WIDTH`/`PAGE_HEIGHT` in `lib/constants.ts`. Stored
  per-page width/height are ignored for layout (kept for a future resize).
- **Editor**: `src/editor/PageEditor.tsx` is lazy-loaded (TipTap is heavy).
  Markdown is the persisted source of truth.
- **Import**: `useFileDrop` → `importService` → for PDFs, `pdfClient` posts to the
  `pdf.worker.ts` Web Worker (pdf.js). Workers keep parsing off the main thread.
- **Export**: `exportMarkdown.ts` (trivial) and `exportPdf.ts` (jsPDF, lazy via
  dynamic import). `markdownToBlocks.ts` flattens markdown for the PDF composer.

## Conventions

- **Styling**: design tokens + resets in `src/styles/global.css`. Each component
  has a sibling `.css` with `component__element` class names. Use CSS variables;
  avoid hardcoded colors/sizes. Highlight palette is in `lib/constants.ts`
  (`HIGHLIGHTS`, `resolveColor`).
- **IDs**: always `createId()` (nanoid). Never integer keys.
- **Deletes**: soft-delete via `doc.remove()` (RxDB sets `_deleted`). Never
  hard-delete.
- **Timestamps**: `updatedAt` is bumped automatically by a preSave hook — don't
  set it manually. It's the future `_modified` for Supabase sync.
- **New persisted field?** Update `types.ts`, `schemas.ts`, and `factories.ts`
  together. Bump the collection schema `version` + add a migration if data exists.
- Keep components small and reusable; prefer composing primitives in
  `src/components/`.

## Gotchas

- RxDB reserves field names starting with `_`; that's why we use `updatedAt`
  rather than a literal `_modified` schema column (the sync layer will map it).
- `@tiptap/pm` has no root export — never list it in `manualChunks`.
- pdf.js runs inside our Web Worker and points `workerSrc` at the bundled worker.
- StrictMode is on; the DB init is a singleton + idempotent seed, so double-mount
  is safe.

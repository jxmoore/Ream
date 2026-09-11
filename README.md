# Ream

> ### ⚠️ This is a concept, not a product.
> Ream is an **experimental prototype** built to explore one idea — nothing more.
> It is not finished, not production-ready, and not maintained or supported.
> Expect rough edges and missing pieces. Please don't rely on it to store
> anything you'd be upset to lose.

## The idea

What if your notes lived in a single, tidy **vertical column** — a document feed
— instead of scattered files, folders, or an endless canvas?

In Ream, each line in the column is one **page** (a markdown note). Drag a page
alongside another and they lock into a **row** of pages sitting side by side, so
related notes can share a line. Everything is left-aligned, so your notes form
one clean spine down the page. Double-click any page to edit it full-screen.

It's **local-first**: everything lives in your browser (IndexedDB). There's no
account, no server, and no network calls — nothing you type leaves your machine.
That also means your notes are tied to the browser you use them in.

## Example usage

![Ream in use — pages in a column, dragged into side-by-side rows](./Example%20usage.gif)

## Try it

Requires Node 18+ (developed on Node 22).

```bash
npm install
```

```bash
npm run dev
```

Then open **http://localhost:5173**.

Other scripts:

```bash
npm run build     # type-check + production build to dist/
npm run preview   # serve the production build locally
npm run typecheck # types only
```

## What you can do

- **Write** — pages are markdown, edited in a full-screen WYSIWYG editor
  (double-click a page to open it). Markdown shortcuts work (`# `, `- `,
  `**bold**`, etc.); changes autosave.
- **Add** — the page-sized **+ Add page** slot at the bottom of the column, or
  the **Page** button in the top bar. A new page you close without touching is
  discarded automatically.
- **Reorder** — drag a page over another's **center** (or into the gap between
  lines) to move it onto its own line; a horizontal bar shows where it will land.
- **Make a row** — drag a page onto another's **edge**, or into the empty space
  **beside** it, to set them side by side; a vertical slot previews the spot.
  Within a row, drag pages left/right to reorder them; drag one out (above or
  below the row) to give it its own line again.
- **Organize rows** — a row's `⋯` menu lets you name it, set a highlight color,
  add badges, export it to PDF, or split it back into separate lines. Drag a row
  by its header grip to move the whole thing up or down.
- **Import** — drag `.md` / `.txt` / `.pdf` files in, or use the **Import**
  button. Text files become pages verbatim; PDFs are text-extracted in a Web
  Worker (multi-page PDFs become a row).
- **Export** — a page to a `.md` file (from its `⋯` menu or the editor), or a
  whole row to a composed PDF (from its `⋯` menu).
- **Canvases** — keep separate boards and switch between them from the dropdown
  next to the canvas name.

## How it's built

React 18 + TypeScript + Vite, with RxDB (on Dexie/IndexedDB) as a reactive local
database, TipTap for the editor, and pdf.js / jsPDF for import/export. Data flows
one way: writes go through a small service layer, and the UI subscribes to
reactive queries. See [`CLAUDE.md`](./CLAUDE.md) for the architecture map.

```
src/
  db/          RxDB setup, schemas, factories, seed, React provider
  services/    All writes (page / group / canvas) — components never touch RxDB directly
  hooks/       Reactive query bridge + typed collection hooks
  canvas/      The column board, rows, page cards, drag + drop geometry
  editor/      Lazy-loaded TipTap editor (markdown is the source of truth)
  import/      File-drop handling + PDF Web Worker
  export/      Markdown + PDF export (lazy-loaded)
  components/  Reusable UI primitives (Button, Modal, Popover, Toast, …)
  lib/         Ids, palette/constants, helpers
  styles/      global.css — design tokens, resets, shared utilities
```

## Scope & what's deliberately missing

This prototype covers the **free, local-only tier** sketched in the
[implementation guide](./ream-implementation-guide.md). Intentionally **not
built** (they're just ideas on paper): cross-device sync, accounts/auth,
subscriptions, "smart import" (OCR + LLM cleanup), and a packaged desktop/mobile
app. There are **no backend services and no secrets** in this repository — it
runs entirely in the browser.

> The original guide imagined an infinite pan/zoom canvas; this prototype
> replaced that with the single vertical column described above. It's a concept
> that's still evolving.

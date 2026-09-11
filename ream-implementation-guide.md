# Ream — Implementation Guide

> A local-first, spatial note-taking app. Notes are markdown "pages" laid out on a large, zoomable canvas. Pan and zoom to see everything at once; zoom into a page to edit it full-screen. Arrange pages into named rows and columns; color them, badge them, and export rows/columns as PDF.

This document is the build spec. It captures decisions that are **already made** — do not relitigate the stack. Build in the phase order given; each phase should be shippable on its own.

---

## 1. Product summary

Ream is "an infinite canvas of notes."

- **Pages**: individual notes, authored in a WYSIWYG editor, stored as markdown.
- **Canvas**: an infinite, pan/zoom surface. Pages are cards positioned at x/y coordinates. Pages and items on the canvas **snap to a grid** when moved or placed, keeping layouts tidy and aligned.
- **Overview ↔ detail**: zoom out to see the whole board; zoom into one page to read/edit it full-screen.
- **Arrangement**: group pages into **columns** and **rows**. Groups have **names**, **highlight colors**, **badges**, etc. — treat this set as open-ended; more group-level attributes and actions may be added over time.
- **Import**: drag-and-drop files onto the canvas → become pages.
- **Export**: a row/column → PDF; a page → markdown file.
- **Cross-platform**: desktop + mobile, sharing one web frontend.
- **Local-first**: works fully offline. Sync across devices is the headline paid feature.

---

## 2. Decisions already made (do not change without reason)

| Concern         | Decision                                                                                  | Why                                                                                         |
| --------------- | ----------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| Frontend        | **React 18 + TypeScript + Vite**                                                          | Portable web core runs in any native shell.                                                 |
| Local database  | **RxDB** on the **Dexie.js (IndexedDB)** storage adapter                                  | Free, production-capable, reactive, offline-first.                                          |
| Storage adapter | Dexie now; SQLite is a **paid** RxDB Premium plugin and a one-line swap later (perf only) | Don't pay for SQLite until perf demands it.                                                 |
| Content format  | **Markdown** stored as a document field — _not_ rich-text JSON, _not_ a custom format     | Portable, exportable, future-proof; users still never see raw markdown unless they want to. |
| Editor          | **TipTap** (`@tiptap/starter-kit` + `@tiptap/markdown`)                                   | WYSIWYG toolbar over a markdown source of truth; battle-tested ProseMirror core.            |
| Cloud backend   | **Supabase** (Postgres + Auth + Realtime + RLS)                                           | Managed Postgres, auth, row-level security out of the box.                                  |
| Sync            | **RxDB Supabase replication plugin**, last-write-wins conflicts                           | Single-user-multi-device case; LWW is sufficient and simplest.                              |
| Payments        | **Stripe** → an `entitlement` flag drives feature gating                                  | Subscription state, not per-feature billing.                                                |
| Native shell    | **Deferred.** Build web-first; choose Tauri / Capacitor / React Native at Phase 8         | The web app is the product; the shell is a wrapper decision.                                |

### Why document/NoSQL and not relational

RxDB is a document store. Even on the SQLite adapter, you do **not** get SQL tables/joins — RxDB wraps storage in a JSON-document model. Model relationships with **references** (store an id) or **embedded fields**, never foreign-key joins.

---

## 3. Hard constraints (these cause silent bugs if missed)

1. **RxDB primary keys must be strings.** Use UUID v4 or nanoid for every record. Never auto-increment integers.
2. **Every synced table needs two system columns:**
   - `_modified` — timestamp of last modification (server default `now()`, auto-updated on write).
   - `_deleted` — boolean. **Soft-delete only.** Never hard-delete synced rows, or offline clients miss the deletion.
3. **Markdown is the source of truth.** TipTap holds ProseMirror JSON in memory for editing, but persist `editor.getMarkdown()` to RxDB. Hydrate the editor with `contentType: 'markdown'`. Do not store ProseMirror JSON as canonical.
4. **Heavy work goes in a Web Worker.** PDF parsing and OCR must run off the main thread or they will jank the canvas. Parse in a worker, then write results into RxDB.
5. **Never ship the LLM API key to the client.** The "smart import" cleanup call goes through a **Supabase Edge Function** (key server-side). The client calls the function, not the model provider.
6. **Sync is a hard gate; client features are soft gates.** Sync physically needs the server, so non-paying users can't do it. OCR/LLM live in the bundle and are gated by a flag — fine, but understand a determined user could flip a client-only flag. Price accordingly (see §7).

---

## 4. Data model

All collections are RxDB collections (JSON schema). String PKs throughout. Each has `_deleted` and `_modified` for replication. Mirror each as a Supabase table with identical fields.

### `canvases`

A board. A user can own several.

```
id: string (uuid, PK)
ownerId: string            // supabase auth user id
name: string
createdAt: number
updatedAt: number
_deleted: boolean
_modified: string (iso timestamp)
```

### `pages`

A note/card.

```
id: string (uuid, PK)
canvasId: string           // ref -> canvases.id
title: string
body: string               // MARKDOWN — source of truth
x: number                  // canvas position
y: number
width: number              // optional card sizing
height: number
groupId: string | null     // ref -> groups.id (column or row membership)
order: number              // sort order within its group
color: string | null       // highlight color (hex or token)
badges: array<{ label: string, color: string }>
createdAt: number
updatedAt: number
_deleted: boolean
_modified: string
```

### `groups`

A column or a row. (One collection, discriminated by `type`.)

```
id: string (uuid, PK)
canvasId: string           // ref -> canvases.id
type: 'column' | 'row'
name: string
color: string | null       // row/column highlight
badges: array<{ label: string, color: string }>
order: number              // position of this group on the canvas
createdAt: number
updatedAt: number
_deleted: boolean
_modified: string
```

### Attachments (images)

Use **RxDB attachments** (Blobs) for images embedded in pages. Reference them from the markdown body via a stable local scheme (e.g. `ream-attachment://<attachmentId>`), and resolve to object URLs at render time. On sync, large binaries are better stored in **Supabase Storage** with the markdown holding the URL — decide per Phase 5 whether images sync via RxDB attachments or Supabase Storage; Storage scales better for big images.

### Supabase mirror (per table)

For each collection above, create a Postgres table with the same columns, `id text primary key`, plus:

```sql
_modified timestamptz not null default now(),
_deleted  boolean    not null default false
-- trigger to auto-update _modified on UPDATE (moddatetime extension)
-- add table to the supabase_realtime publication for live pull
-- RLS: owner-only access keyed on ownerId = auth.uid()
```

---

## 5. Architecture notes

- **Reactivity**: RxDB reactive queries drive React state. Subscribe to queries; the canvas re-renders as documents change. This also makes sync "just appear" — replicated changes flow through the same query subscriptions.
- **One canvas viewport, many cards**: render a pan/zoom container; cards are absolutely positioned by `x/y` and scaled by the viewport transform. Virtualize/cull off-screen cards once boards get large.
- **Editing model**: clicking a card zooms it to full-screen and mounts a TipTap editor. On blur / debounce, serialize to markdown and write to RxDB. Keep the lightweight markdown render (read-mode) separate from the heavy TipTap instance so you only instantiate TipTap for the focused page.
- **Gating**: a single `useEntitlement()` hook reads subscription state. Sync replication only starts when entitled. Premium import paths check the same hook.

### Suggested libraries

- **Canvas pan/zoom**: `react-zoom-pan-pinch` (simple transform wrapper) for MVP, or a custom CSS-transform viewport. `@xyflow/react` (React Flow) is an option if you want node dragging/auto-layout for free, but it's opinionated toward node-graphs — evaluate, don't assume.
- **Drag & drop** (cards into groups, reordering): `@dnd-kit/core`.
- **Editor**: `@tiptap/react`, `@tiptap/starter-kit`, `@tiptap/markdown`.
- **PDF text**: `pdfjs-dist` (pdf.js), run in a worker.
- **OCR**: `tesseract.js`, run in a worker (premium).
- **PDF export**: `pdf-lib` or `jspdf` (or render-to-print). Compose the cards of a group into a document.
- **IDs**: `nanoid`.

---

## 6. Build phases

Ship each phase independently. Local-first value lands before any backend exists.

### Phase 0 — Scaffold

- Vite + React + TS project.
- RxDB with Dexie storage. Enable `RxDBDevModePlugin` in dev and AJV schema validation.
- Define the four collections from §4. Seed a default canvas.

### Phase 1 — Canvas + pages (local only)

- Pan/zoom canvas viewport.
- Create / move / delete page cards (positions persist to RxDB).
- Zoom-into-page full-screen view with the **TipTap WYSIWYG editor** (bold, italic, underline, headings, lists, links, code). Persist `getMarkdown()`. Accept pasted/typed raw markdown via `contentType: 'markdown'`.
- Read-mode markdown rendering on the card face.

### Phase 2 — Arrangement

- Columns and rows (`groups`). Drag cards into a group; reorder within a group (`order`).
- Name a group; set a highlight **color**; add **badges**.
- Visual treatment of grouped cards (column/row lanes).

### Phase 3 — Import (free tier)

- Drag-and-drop onto the canvas.
- **.md / .txt**: read via File API, create a page at the drop point (trivial — text is already the format).
- **PDF (basic)**: extract text with pdf.js **in a worker**, create page(s). Lossy by nature — set expectations in UI copy. A multi-page PDF may split into multiple page-cards auto-dropped into a new named column.

### Phase 4 — Export

- **Page → markdown**: write the stored `body` to a `.md` file.
- **Row/column → PDF**: compose the group's pages into a PDF (`pdf-lib`/`jspdf`).

### Phase 5 — Auth + Sync (paid gate begins)

- Supabase Auth (email/OAuth).
- Create Supabase mirror tables with `_modified` / `_deleted`, triggers, realtime publication, and owner-scoped RLS.
- Wire **RxDB Supabase replication** per collection. LWW conflict handler (default).
- Start replication **only for entitled users**. Free users stay fully local.

### Phase 6 — Subscriptions

- Stripe checkout + webhook → persist subscription state → `entitlement` flag.
- `useEntitlement()` hook gates sync (Phase 5) and premium import (Phase 7).

### Phase 7 — Premium import ("smart import")

- **OCR**: scanned/image PDFs via tesseract.js in a worker.
- **LLM clean-up**: pipe raw extracted text through an LLM to reconstruct headings, lists, and tables into clean markdown. Call a **Supabase Edge Function** that holds the model API key; the client never sees it. Online-only, has real per-use cost — this is the most defensible premium feature.
- **Multi-page auto-columns**: split a long PDF into page-cards arranged in a named column.

### Phase 8 — Native shell

- Decide the wrapper. The web app is already the product; this just packages it.
- Options: Tauri 2 (desktop-strong, smaller binaries), Capacitor (mobile-strong, matches an existing RN/Capacitor workflow), or React Native (if mobile becomes co-equal — note RN would mean re-housing the React DOM UI). **Spike the chosen shell's IndexedDB/OPFS behavior** before committing, since RxDB's Dexie storage relies on it inside the webview.

---

## 7. Feature tiering

| Capability                                       | Free | Paid                        |
| ------------------------------------------------ | ---- | --------------------------- |
| Local notes, canvas, arrangement, colors, badges | ✅   | ✅                          |
| Markdown / TXT import                            | ✅   | ✅                          |
| Basic PDF text extraction (pdf.js)               | ✅   | ✅                          |
| Export (markdown, PDF)                           | ✅   | ✅                          |
| **Cross-device sync + backup**                   | —    | ✅ (hard gate: server-side) |
| **OCR** (scanned PDFs)                           | —    | ✅                          |
| **LLM smart import** (clean structured markdown) | —    | ✅ (online, real cost)      |
| **Multi-page PDF → auto-columns**                | —    | ✅                          |

Pattern: free "works roughly," paid "works great." The paid tier concentrates the features that are either server-dependent (sync) or carry real marginal cost (LLM) — the easiest things to charge for.

---

## 8. Gotchas recap

- String PKs everywhere (nanoid/uuid).
- `_modified` + `_deleted` on every synced table; soft-delete only.
- Persist markdown, not ProseMirror JSON.
- Workers for PDF/OCR.
- LLM key server-side (Edge Function), never in client.
- Dexie storage now; SQLite is a paid perf swap, not a rewrite.
- Replication starts only when entitled.

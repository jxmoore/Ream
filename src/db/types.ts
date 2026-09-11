/* ===========================================================================
   Domain types — these mirror the RxDB JSON schemas in `schemas.ts` and the
   §4 data model in the implementation guide.

   Sync note (Phase 5, later): RxDB reserves field names beginning with "_"
   (it owns `_deleted`, `_meta`, `_rev`). So instead of a literal `_modified`
   column we keep `updatedAt` (epoch ms) on every record and bump it on each
   write. When Supabase replication is wired up, the push/pull mappers will
   translate `updatedAt` <-> `_modified` and RxDB's soft-delete <-> `_deleted`.
   =========================================================================== */

export type GroupType = 'column' | 'row';

/** A colored, labeled badge attached to a page or group. */
export interface Badge {
  label: string;
  /** A highlight token key (see HIGHLIGHTS) or raw hex. */
  color: string;
}

/** A board. A user can own several. */
export interface Canvas {
  id: string;
  ownerId: string;
  name: string;
  createdAt: number;
  updatedAt: number;
}

/** A note / card. `body` is markdown and is the source of truth. */
export interface Page {
  id: string;
  canvasId: string;
  title: string;
  /** MARKDOWN — the canonical content (never ProseMirror JSON). */
  body: string;
  x: number;
  y: number;
  width: number;
  height: number;
  /** Membership in a column or row, or null for a free-floating card. */
  groupId: string | null;
  /** Sort order within its group. */
  order: number;
  /** Highlight color: a HIGHLIGHTS token key or raw hex, or null. */
  color: string | null;
  badges: Badge[];
  createdAt: number;
  updatedAt: number;
}

/** A column or a row. One collection, discriminated by `type`. */
export interface Group {
  id: string;
  canvasId: string;
  type: GroupType;
  name: string;
  color: string | null;
  badges: Badge[];
  /** Sort order of this group on the canvas. */
  order: number;
  /** Canvas position of the lane (a Ream extension — group attrs are
   *  explicitly open-ended in §4). */
  x: number;
  y: number;
  createdAt: number;
  updatedAt: number;
}

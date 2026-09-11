/* Page write operations. Components call these instead of touching RxDB
   directly, so persistence logic stays in one place. */

import type { ReamDatabase } from '../db/database';
import type { Page } from '../db/types';
import { buildPage } from '../db/factories';

export type NewPageInput = Partial<Page> & { canvasId: string };

export async function createPage(
  db: ReamDatabase,
  input: NewPageInput,
): Promise<Page> {
  const page = buildPage(input);
  await db.pages.insert(page);
  return page;
}

/** Insert several pages at once (e.g. a multi-page PDF import). */
export async function createPages(
  db: ReamDatabase,
  inputs: NewPageInput[],
): Promise<Page[]> {
  const pages = inputs.map(buildPage);
  await db.pages.bulkInsert(pages);
  return pages;
}

export async function patchPage(
  db: ReamDatabase,
  id: string,
  patch: Partial<Page>,
): Promise<void> {
  const doc = await db.pages.findOne(id).exec();
  if (!doc) return;
  await doc.incrementalPatch(patch);
}

export async function movePage(
  db: ReamDatabase,
  id: string,
  x: number,
  y: number,
): Promise<void> {
  await patchPage(db, id, { x, y });
}

/** Soft-delete (§3: never hard-delete). RxDB sets `_deleted = true`. */
export async function deletePage(
  db: ReamDatabase,
  id: string,
): Promise<void> {
  const doc = await db.pages.findOne(id).exec();
  await doc?.remove();
}

/* --- Grouping ---------------------------------------------------------- */

/** Move a page into a group at a given position, renumbering siblings. */
export async function assignPageToGroup(
  db: ReamDatabase,
  pageId: string,
  groupId: string,
  index: number,
): Promise<void> {
  const siblings = await db.pages
    .find({
      selector: { groupId, id: { $ne: pageId } },
      sort: [{ order: 'asc' }],
    })
    .exec();

  const ordered = siblings.map((d) => d.id);
  const clampedIndex = Math.max(0, Math.min(index, ordered.length));
  ordered.splice(clampedIndex, 0, pageId);

  await Promise.all(
    ordered.map((id, i) =>
      patchPage(db, id, id === pageId ? { groupId, order: i } : { order: i }),
    ),
  );
}

/** Remove a page from its group and drop it at a free canvas position. */
export async function detachPageFromGroup(
  db: ReamDatabase,
  pageId: string,
  x: number,
  y: number,
): Promise<void> {
  await patchPage(db, pageId, { groupId: null, order: 0, x, y });
}

/** Persist a new ordering for the pages of a group. */
export async function reorderPagesInGroup(
  db: ReamDatabase,
  orderedPageIds: string[],
): Promise<void> {
  await Promise.all(
    orderedPageIds.map((id, i) => patchPage(db, id, { order: i })),
  );
}

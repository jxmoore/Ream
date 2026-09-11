/* Column document model: the board is a vertical, ordered list of ROWS, and
   every page belongs to a row. A 1-page row renders as a bare page; a 2+ page
   row renders as a row of pages. Column position = group.order; horizontal
   position within a row = page.order. These services own all structural edits;
   components never reorder RxDB docs directly. */

import type { ReamDatabase } from '../db/database';
import type { Page } from '../db/types';
import { createGroup } from './groupService';
import { createPage } from './pageService';
import { clamp } from '../lib/grid';

/* --- Reads ------------------------------------------------------------- */

async function groupIdsInOrder(
  db: ReamDatabase,
  canvasId: string,
): Promise<string[]> {
  const groups = await db.groups
    .find({ selector: { canvasId }, sort: [{ order: 'asc' }, { createdAt: 'asc' }] })
    .exec();
  return groups.map((g) => g.id);
}

async function memberIdsInOrder(
  db: ReamDatabase,
  groupId: string,
): Promise<string[]> {
  const members = await db.pages
    .find({ selector: { groupId }, sort: [{ order: 'asc' }, { createdAt: 'asc' }] })
    .exec();
  return members.map((m) => m.id);
}

/* --- Renumber helpers -------------------------------------------------- */

async function writeColumnOrder(
  db: ReamDatabase,
  orderedGroupIds: string[],
): Promise<void> {
  await Promise.all(
    orderedGroupIds.map(async (id, i) => {
      const g = await db.groups.findOne(id).exec();
      await g?.incrementalPatch({ order: i });
    }),
  );
}

async function writeRowOrder(
  db: ReamDatabase,
  orderedPageIds: string[],
  groupId: string | null,
): Promise<void> {
  await Promise.all(
    orderedPageIds.map(async (id, i) => {
      const p = await db.pages.findOne(id).exec();
      await p?.incrementalPatch({ order: i, groupId });
    }),
  );
}

/** Remove a group if it has no members, and re-pack column orders. */
async function cleanupGroup(
  db: ReamDatabase,
  canvasId: string,
  groupId: string,
): Promise<void> {
  const members = await memberIdsInOrder(db, groupId);
  if (members.length > 0) return;
  const doc = await db.groups.findOne(groupId).exec();
  await doc?.remove();
  await writeColumnOrder(db, await groupIdsInOrder(db, canvasId));
}

/* --- Creation ---------------------------------------------------------- */

/** Append a new single-page row at the bottom of the column. */
export async function appendPageLine(
  db: ReamDatabase,
  canvasId: string,
): Promise<Page> {
  const order = (await groupIdsInOrder(db, canvasId)).length;
  const group = await createGroup(db, { canvasId, type: 'row', order });
  return createPage(db, { canvasId, groupId: group.id, order: 0 });
}

/** Append a new page to the end of an existing row. */
export async function addPageToRow(
  db: ReamDatabase,
  canvasId: string,
  groupId: string,
): Promise<Page> {
  const order = (await memberIdsInOrder(db, groupId)).length;
  return createPage(db, { canvasId, groupId, order });
}

/** Create a row of pages at the bottom (e.g. a multi-page import). */
export async function appendRow(
  db: ReamDatabase,
  canvasId: string,
  pages: Array<Partial<Page>>,
  rowName = '',
): Promise<void> {
  const order = (await groupIdsInOrder(db, canvasId)).length;
  const group = await createGroup(db, {
    canvasId,
    type: 'row',
    order,
    name: pages.length > 1 ? rowName : '',
  });
  await Promise.all(
    pages.map((p, i) =>
      createPage(db, { ...p, canvasId, groupId: group.id, order: i }),
    ),
  );
}

/* --- Moves ------------------------------------------------------------- */

/** Move a page into a row at a horizontal index (or reorder within its row). */
export async function movePageIntoRow(
  db: ReamDatabase,
  canvasId: string,
  pageId: string,
  targetGroupId: string,
  index: number,
): Promise<void> {
  const page = await db.pages.findOne(pageId).exec();
  if (!page) return;
  const oldGroupId = page.groupId;

  const target = (await memberIdsInOrder(db, targetGroupId)).filter(
    (id) => id !== pageId,
  );
  target.splice(clamp(index, 0, target.length), 0, pageId);
  await writeRowOrder(db, target, targetGroupId);

  if (oldGroupId && oldGroupId !== targetGroupId) {
    await writeRowOrder(
      db,
      (await memberIdsInOrder(db, oldGroupId)).filter((id) => id !== pageId),
      oldGroupId,
    );
    await cleanupGroup(db, canvasId, oldGroupId);
  }
}

/**
 * Move a page onto its own line, inserted before `beforeGroupId` (or at the end
 * when null). If the page already is its own line, just reorders the column.
 */
export async function movePageToLine(
  db: ReamDatabase,
  canvasId: string,
  pageId: string,
  beforeGroupId: string | null,
): Promise<void> {
  const page = await db.pages.findOne(pageId).exec();
  if (!page) return;
  const oldGroupId = page.groupId;
  const oldMembers = oldGroupId ? await memberIdsInOrder(db, oldGroupId) : [];

  const insert = (list: string[], id: string) => {
    const without = list.filter((g) => g !== id);
    const at = beforeGroupId ? without.indexOf(beforeGroupId) : -1;
    without.splice(at < 0 ? without.length : at, 0, id);
    return without;
  };

  if (oldGroupId && oldMembers.length === 1) {
    // Already its own line — just move that row in the column.
    if (beforeGroupId === oldGroupId) return; // dropped onto itself
    await writeColumnOrder(
      db,
      insert(await groupIdsInOrder(db, canvasId), oldGroupId),
    );
    return;
  }

  // Split out into a brand-new single-page row.
  const newGroup = await createGroup(db, { canvasId, type: 'row', order: 0 });
  await page.incrementalPatch({ groupId: newGroup.id, order: 0 });
  if (oldGroupId) {
    await writeRowOrder(
      db,
      oldMembers.filter((id) => id !== pageId),
      oldGroupId,
    );
  }
  await writeColumnOrder(
    db,
    insert(await groupIdsInOrder(db, canvasId), newGroup.id),
  );
}

/** Move a whole row line before `beforeGroupId` (or to the end when null). */
export async function moveRowToLine(
  db: ReamDatabase,
  canvasId: string,
  groupId: string,
  beforeGroupId: string | null,
): Promise<void> {
  if (beforeGroupId === groupId) return;
  const ids = (await groupIdsInOrder(db, canvasId)).filter((g) => g !== groupId);
  const at = beforeGroupId ? ids.indexOf(beforeGroupId) : -1;
  ids.splice(at < 0 ? ids.length : at, 0, groupId);
  await writeColumnOrder(db, ids);
}

/** Split a row into one single-page line per member, in place. */
export async function ungroupRow(
  db: ReamDatabase,
  canvasId: string,
  groupId: string,
): Promise<void> {
  const members = await memberIdsInOrder(db, groupId);
  if (members.length <= 1) return;
  const columns = await groupIdsInOrder(db, canvasId);
  const at = columns.indexOf(groupId);

  // Keep the first member in the existing group; give the rest new rows.
  const newGroupIds: string[] = [groupId];
  for (let i = 1; i < members.length; i++) {
    const g = await createGroup(db, { canvasId, type: 'row', order: 0 });
    const p = await db.pages.findOne(members[i]).exec();
    await p?.incrementalPatch({ groupId: g.id, order: 0 });
    newGroupIds.push(g.id);
  }
  const first = await db.pages.findOne(members[0]).exec();
  await first?.incrementalPatch({ order: 0 });

  columns.splice(at, 1, ...newGroupIds);
  await writeColumnOrder(db, columns);
}

/* --- Deletion ---------------------------------------------------------- */

/** Soft-delete a page and remove its row if it becomes empty. */
export async function removePage(
  db: ReamDatabase,
  canvasId: string,
  pageId: string,
): Promise<void> {
  const page = await db.pages.findOne(pageId).exec();
  if (!page) return;
  const groupId = page.groupId;
  await page.remove();
  if (groupId) {
    await writeRowOrder(
      db,
      (await memberIdsInOrder(db, groupId)).filter((id) => id !== pageId),
      groupId,
    );
    await cleanupGroup(db, canvasId, groupId);
  }
}

/* --- Migration --------------------------------------------------------- */

/** Wrap any orphan pages (groupId null) into their own rows at the bottom. */
export async function normalizeColumn(
  db: ReamDatabase,
  canvasId: string,
): Promise<void> {
  const orphans = await db.pages
    .find({ selector: { canvasId, groupId: null } })
    .exec();
  for (const p of orphans) {
    const order = (await groupIdsInOrder(db, canvasId)).length;
    const group = await createGroup(db, { canvasId, type: 'row', order });
    await p.incrementalPatch({ groupId: group.id, order: 0 });
  }
}

/* Group (column/row) write operations. */

import type { ReamDatabase } from '../db/database';
import type { Group, GroupType } from '../db/types';
import { buildGroup } from '../db/factories';
import { patchPage } from './pageService';

export type NewGroupInput = Partial<Group> & {
  canvasId: string;
  type: GroupType;
};

export async function createGroup(
  db: ReamDatabase,
  input: NewGroupInput,
): Promise<Group> {
  const group = buildGroup(input);
  await db.groups.insert(group);
  return group;
}

export async function patchGroup(
  db: ReamDatabase,
  id: string,
  patch: Partial<Group>,
): Promise<void> {
  const doc = await db.groups.findOne(id).exec();
  if (!doc) return;
  await doc.incrementalPatch(patch);
}

export async function moveGroup(
  db: ReamDatabase,
  id: string,
  x: number,
  y: number,
): Promise<void> {
  await patchGroup(db, id, { x, y });
}

/** Soft-delete the group and release its member pages back onto the canvas. */
export async function deleteGroup(
  db: ReamDatabase,
  id: string,
  releaseAt?: { x: number; y: number },
): Promise<void> {
  const members = await db.pages.find({ selector: { groupId: id } }).exec();
  await Promise.all(
    members.map((page, i) =>
      patchPage(db, page.id, {
        groupId: null,
        order: 0,
        x: (releaseAt?.x ?? 0) + i * 24,
        y: (releaseAt?.y ?? 0) + i * 24,
      }),
    ),
  );
  const doc = await db.groups.findOne(id).exec();
  await doc?.remove();
}

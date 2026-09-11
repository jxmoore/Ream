/* Canvas write operations. */

import type { ReamDatabase } from '../db/database';
import type { Canvas } from '../db/types';
import { buildCanvas } from '../db/factories';

export async function createCanvas(
  db: ReamDatabase,
  name: string,
): Promise<Canvas> {
  const canvas = buildCanvas({ name });
  await db.canvases.insert(canvas);
  return canvas;
}

export async function renameCanvas(
  db: ReamDatabase,
  id: string,
  name: string,
): Promise<void> {
  const doc = await db.canvases.findOne(id).exec();
  await doc?.incrementalPatch({ name });
}

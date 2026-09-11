/* Seeds a default canvas (and a welcome page) on first run so the app is never
   empty. Idempotent: it no-ops once any canvas exists. */

import type { ReamDatabase } from './database';
import { buildCanvas, buildGroup, buildPage } from './factories';

const WELCOME_BODY = `# Welcome to Ream 👋

This is a **page** — a markdown note. Your pages live in a single column, one
line after another.

- **Add a page**: the **+ Add page** button at the bottom.
- **Edit**: double-click a page to open the full-screen editor.
- **Reorder**: drag a page up or down the column.
- **Make a row**: drag a page onto another page's **side** to set them side by
  side. Drag onto a page's middle to drop it above or below instead.

Drag in \`.md\`, \`.txt\`, or \`.pdf\` files to import them as new pages.
`;

/** Ensures a canvas exists and returns the id of the canvas to open. */
export async function seedDefaultCanvas(db: ReamDatabase): Promise<string> {
  const existing = await db.canvases
    .findOne({ sort: [{ createdAt: 'asc' }] })
    .exec();
  if (existing) return existing.id;

  const canvas = buildCanvas({ name: 'My first canvas' });
  await db.canvases.insert(canvas);

  // The welcome page is a single-page row at the top of the column.
  const group = buildGroup({ canvasId: canvas.id, type: 'row', order: 0 });
  await db.groups.insert(group);
  await db.pages.insert(
    buildPage({
      canvasId: canvas.id,
      groupId: group.id,
      order: 0,
      title: 'Welcome to Ream',
      body: WELCOME_BODY,
    }),
  );

  return canvas.id;
}

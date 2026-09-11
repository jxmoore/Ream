/* Convenience hooks for the reactive collections, scoped to a canvas. */

import { useRxData } from './useRxData';
import type { Canvas, Page, Group } from '../db/types';

export function useCanvases(): Canvas[] {
  return useRxData<Canvas>(
    (db) => db.canvases.find({ sort: [{ createdAt: 'asc' }] }),
    [],
  );
}

export function useCanvas(canvasId: string | null): Canvas | null {
  const result = useRxData<Canvas>(
    (db) => (canvasId ? db.canvases.findOne(canvasId) : null),
    [canvasId],
  );
  return result[0] ?? null;
}

export function usePages(canvasId: string | null): Page[] {
  return useRxData<Page>(
    (db) =>
      canvasId
        ? db.pages.find({
            selector: { canvasId },
            sort: [{ createdAt: 'asc' }],
          })
        : null,
    [canvasId],
  );
}

export function useGroups(canvasId: string | null): Group[] {
  return useRxData<Group>(
    (db) =>
      canvasId
        ? db.groups.find({
            selector: { canvasId },
            sort: [{ order: 'asc' }],
          })
        : null,
    [canvasId],
  );
}

/* Owns dragging in the column:
   - Page drag: reorder onto its own line, or merge into a row.
   - Row drag (by the row's grip): reorder the whole row vertically.

   A small movement threshold keeps clicks/double-clicks from committing a move.
   Handlers are identity-stable via refs so a re-render mid-drag can't orphan a
   listener. */

import { useCallback, useEffect, useRef, useState } from 'react';
import type { ReamDatabase } from '../db/database';
import type { Page, Group } from '../db/types';
import {
  movePageIntoRow,
  movePageToLine,
  moveRowToLine,
} from '../services/columnService';
import { computeDrop, type Drop } from './columnDrop';

const DRAG_THRESHOLD = 4;

export interface DragState {
  kind: 'page' | 'row';
  id: string;
  width: number;
  height: number;
  left: number;
  top: number;
  drop: Drop;
}

interface ColumnDragController {
  dragState: DragState | null;
  startPageDrag: (e: React.PointerEvent, page: Page) => void;
  startRowDrag: (e: React.PointerEvent, group: Group) => void;
}

export function useColumnDrag(
  db: ReamDatabase,
  canvasId: string,
): ColumnDragController {
  const [dragState, setDragState] = useState<DragState | null>(null);
  const stateRef = useRef<DragState | null>(null);
  const canvasIdRef = useRef(canvasId);
  canvasIdRef.current = canvasId;

  const grab = useRef({
    kind: 'page' as 'page' | 'row',
    id: '',
    groupId: '', // the dragged page's current row (page drags)
    dx: 0,
    dy: 0,
    startX: 0,
    startY: 0,
    width: 0,
    height: 0,
    moved: false,
  });

  const moveRef = useRef<(e: PointerEvent) => void>(() => {});
  const upRef = useRef<(e: PointerEvent) => void>(() => {});
  const cancelRef = useRef<() => void>(() => {});

  const set = useCallback((next: DragState | null) => {
    stateRef.current = next;
    setDragState(next);
  }, []);

  useEffect(() => {
    moveRef.current = (e: PointerEvent) => {
      const g = grab.current;
      if (!g.id) return;
      if (!g.moved) {
        if (Math.hypot(e.clientX - g.startX, e.clientY - g.startY) < DRAG_THRESHOLD) {
          return;
        }
        g.moved = true;
        document.body.classList.add('is-dragging-card');
      }
      const drop = computeDrop(e.clientX, e.clientY, {
        allowMerge: g.kind === 'page',
        draggedPageId: g.kind === 'page' ? g.id : undefined,
        draggedPageGroupId: g.kind === 'page' ? g.groupId : undefined,
        draggedGroupId: g.kind === 'row' ? g.id : undefined,
      });
      set({
        kind: g.kind,
        id: g.id,
        width: g.width,
        height: g.height,
        left: e.clientX - g.dx,
        top: e.clientY - g.dy,
        drop,
      });
    };

    const teardown = () => {
      window.removeEventListener('pointermove', moveRef.current);
      window.removeEventListener('pointerup', upRef.current);
      window.removeEventListener('pointercancel', cancelRef.current);
      document.body.classList.remove('is-dragging-card');
    };

    cancelRef.current = () => {
      teardown();
      set(null);
      grab.current = { ...grab.current, id: '', moved: false };
    };

    upRef.current = () => {
      const g = grab.current;
      const s = stateRef.current;
      teardown();
      set(null);
      const { id, kind, moved } = g;
      grab.current = { ...g, id: '', moved: false };
      if (!moved || !id) return;

      const cv = canvasIdRef.current;
      const drop = s?.drop;
      void (async () => {
        if (kind === 'row') {
          if (drop?.kind === 'new-line') {
            await moveRowToLine(db, cv, id, drop.beforeGroupId);
          }
          return;
        }
        if (drop?.kind === 'into-row') {
          await movePageIntoRow(db, cv, id, drop.groupId, drop.index);
        } else if (drop?.kind === 'new-line') {
          await movePageToLine(db, cv, id, drop.beforeGroupId);
        }
      })();
    };
  }, [db, set]);

  const begin = useCallback(
    (
      e: React.PointerEvent,
      kind: 'page' | 'row',
      id: string,
      groupId: string,
      rectEl: HTMLElement | null,
    ) => {
      if (e.button !== 0) return;
      e.stopPropagation();
      const rect = (rectEl ?? (e.currentTarget as HTMLElement)).getBoundingClientRect();
      grab.current = {
        kind,
        id,
        groupId,
        dx: e.clientX - rect.left,
        dy: e.clientY - rect.top,
        startX: e.clientX,
        startY: e.clientY,
        width: rect.width,
        height: rect.height,
        moved: false,
      };
      window.addEventListener('pointermove', moveRef.current);
      window.addEventListener('pointerup', upRef.current);
      window.addEventListener('pointercancel', cancelRef.current);
    },
    [],
  );

  const startPageDrag = useCallback(
    (e: React.PointerEvent, page: Page) => {
      const card = (e.currentTarget as HTMLElement).closest<HTMLElement>(
        '[data-page-id]',
      );
      begin(e, 'page', page.id, page.groupId ?? '', card);
    },
    [begin],
  );

  const startRowDrag = useCallback(
    (e: React.PointerEvent, group: Group) => {
      const row = (e.currentTarget as HTMLElement).closest<HTMLElement>(
        '[data-row-id]',
      );
      begin(e, 'row', group.id, '', row);
    },
    [begin],
  );

  return { dragState, startPageDrag, startRowDrag };
}

/* Drop-target geometry for the column. DOM-based (screen space) so it accounts
   for scrolling without any coordinate math.

   - Dragging a page WITHIN its own row → reorder left/right inside that row
     (by horizontal position); it only leaves the row when dragged clearly above
     or below it.
   - Over a DIFFERENT page's center (or a vertical gap between lines) → reorder
     onto its own line (a horizontal bar previews it).
   - Over a different page's left/right EDGE, or BESIDE it (empty space / gap) →
     merge into that row at that spot (a vertical slot previews it). Overlaying
     the first page's left edge inserts at the front.
   - Dragging a whole row only reorders vertically. */

export type Drop =
  | { kind: 'new-line'; beforeGroupId: string | null }
  | { kind: 'into-row'; groupId: string; index: number }
  | null;

interface Options {
  draggedPageId?: string;
  /** The row the dragged page currently belongs to (page drags only). */
  draggedPageGroupId?: string;
  /** The row being dragged (row drags only) — excluded as a target. */
  draggedGroupId?: string;
  allowMerge: boolean;
}

function rowElements(): HTMLElement[] {
  return Array.from(document.querySelectorAll<HTMLElement>('[data-row-id]'));
}

/** Insertion index within a row by horizontal position, ignoring dragged. */
function indexByX(cards: HTMLElement[], clientX: number): number {
  let index = 0;
  for (const c of cards) {
    const r = c.getBoundingClientRect();
    if (clientX > r.left + r.width / 2) index++;
  }
  return index;
}

/** New-line target: which row the insertion bar sits above (null = the end). */
function newLineTarget(
  rows: HTMLElement[],
  clientY: number,
  rect: DOMRect,
  rowId: string,
  index: number,
): Drop {
  const before = clientY < rect.top + rect.height / 2;
  if (before) return { kind: 'new-line', beforeGroupId: rowId };
  const next = rows[index + 1];
  return { kind: 'new-line', beforeGroupId: next?.dataset.rowId ?? null };
}

export function computeDrop(
  clientX: number,
  clientY: number,
  opts: Options,
): Drop {
  const rows = rowElements().filter(
    (r) => r.dataset.rowId !== opts.draggedGroupId,
  );
  if (rows.length === 0) return { kind: 'new-line', beforeGroupId: null };

  for (let i = 0; i < rows.length; i++) {
    const rowEl = rows[i];
    const rect = rowEl.getBoundingClientRect();
    if (clientY < rect.top || clientY > rect.bottom) continue;

    const rowId = rowEl.dataset.rowId!;

    // Row drags only reorder lines.
    if (!opts.allowMerge) {
      return newLineTarget(rows, clientY, rect, rowId, i);
    }

    const cards = Array.from(
      rowEl.querySelectorAll<HTMLElement>('[data-page-id]'),
    ).filter((c) => c.dataset.pageId !== opts.draggedPageId);

    // Inside the page's OWN row → reorder within the row by horizontal
    // position. (It only leaves the row when dragged out of this band.)
    if (rowId === opts.draggedPageGroupId) {
      if (cards.length === 0) {
        return newLineTarget(rows, clientY, rect, rowId, i);
      }
      return { kind: 'into-row', groupId: rowId, index: indexByX(cards, clientX) };
    }

    // The dragged page's own single-page line (when it's not a real row).
    if (cards.length === 0) {
      return newLineTarget(rows, clientY, rect, rowId, i);
    }

    // A different page: its center reorders onto its own line; its left/right
    // edge merges into this row.
    const over = cards.find((c) => {
      const r = c.getBoundingClientRect();
      return clientX >= r.left && clientX <= r.right;
    });
    if (over) {
      const r = over.getBoundingClientRect();
      const relX = (clientX - r.left) / r.width;
      const idx = cards.indexOf(over);
      if (relX < 0.33) return { kind: 'into-row', groupId: rowId, index: idx };
      if (relX > 0.67)
        return { kind: 'into-row', groupId: rowId, index: idx + 1 };
      return newLineTarget(rows, clientY, rect, rowId, i);
    }

    // Beside the pages (to the right/left, or a gap) → merge into this row.
    return { kind: 'into-row', groupId: rowId, index: indexByX(cards, clientX) };
  }

  // Between rows / above / below everything → reorder at that boundary.
  const firstBelow = rows.find((r) => clientY < r.getBoundingClientRect().top);
  return { kind: 'new-line', beforeGroupId: firstBelow?.dataset.rowId ?? null };
}

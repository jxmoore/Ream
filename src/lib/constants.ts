/* Shared constants: highlight palette, canvas geometry, and card defaults. */

export interface Highlight {
  key: string;
  label: string;
  hex: string;
}

/** Highlight colors for pages and groups. Keys are stored; hex is for render. */
export const HIGHLIGHTS: Highlight[] = [
  { key: 'slate', label: 'Slate', hex: '#64748b' },
  { key: 'red', label: 'Red', hex: '#ef4444' },
  { key: 'orange', label: 'Orange', hex: '#f97316' },
  { key: 'amber', label: 'Amber', hex: '#f59e0b' },
  { key: 'green', label: 'Green', hex: '#22c55e' },
  { key: 'teal', label: 'Teal', hex: '#14b8a6' },
  { key: 'blue', label: 'Blue', hex: '#3b82f6' },
  { key: 'violet', label: 'Violet', hex: '#8b5cf6' },
  { key: 'pink', label: 'Pink', hex: '#ec4899' },
];

const HIGHLIGHT_BY_KEY = new Map(HIGHLIGHTS.map((h) => [h.key, h]));

/** Resolve a stored color (token key or raw hex) to a render-ready CSS color. */
export function resolveColor(color: string | null | undefined): string | null {
  if (!color) return null;
  const token = HIGHLIGHT_BY_KEY.get(color);
  return token ? token.hex : color;
}

/* --- Canvas geometry --------------------------------------------------- */

/** World-space grid step that cards snap to (matches --grid-size). */
export const GRID_SIZE = 24;

export const MIN_ZOOM = 0.1;
export const MAX_ZOOM = 2.5;
export const DEFAULT_ZOOM = 1;

/* --- Card defaults ----------------------------------------------------- */

/** US-Letter aspect ratio (8.5 × 11). Free cards render as portrait sheets. */
export const PAPER_W_OVER_H = 8.5 / 11;

/** Height of a paper-shaped card for a given width. */
export function paperHeight(width: number): number {
  return Math.round(width / PAPER_W_OVER_H);
}

/** The single, uniform page size. Every card renders at this size regardless
 *  of content (keep in sync with --page-width in global.css). */
export const PAGE_WIDTH = 300;
export const PAGE_HEIGHT = paperHeight(PAGE_WIDTH); // ≈ 388

// Back-compat aliases.
export const DEFAULT_CARD_WIDTH = PAGE_WIDTH;
export const DEFAULT_CARD_HEIGHT = PAGE_HEIGHT;

/** How close (as a fraction of a card's smaller side) two pages must land to
 *  magnetically lock into a row/column on drop. */
export const SNAP_FRACTION = 0.5;
/** Minimum cross-axis overlap (fraction) for an adjacency to count. */
export const SNAP_ALIGN_FRACTION = 0.35;

/** Lane sizing for group containers. */
export const LANE_PADDING = 16;
export const LANE_HEADER_HEIGHT = 44;
export const LANE_GAP = 12;

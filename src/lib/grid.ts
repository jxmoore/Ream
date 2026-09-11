import { GRID_SIZE } from './constants';

/** Snap a single world-space coordinate to the grid. */
export function snap(value: number, grid = GRID_SIZE): number {
  return Math.round(value / grid) * grid;
}

/** Snap an {x, y} world-space point to the grid. */
export function snapPoint(
  x: number,
  y: number,
  grid = GRID_SIZE,
): { x: number; y: number } {
  return { x: snap(x, grid), y: snap(y, grid) };
}

/** Clamp a number into [min, max]. */
export function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

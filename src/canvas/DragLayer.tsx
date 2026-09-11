/* Floating "ghost" of the page being dragged, following the pointer. */

import { createPortal } from 'react-dom';
import type { Page } from '../db/types';
import { resolveColor } from '../lib/constants';
import './DragLayer.css';

interface DragLayerProps {
  page: Page | undefined;
  left: number;
  top: number;
  width: number;
  height: number;
}

export function DragLayer({ page, left, top, width, height }: DragLayerProps) {
  if (!page) return null;
  const accent = resolveColor(page.color);

  return createPortal(
    <div
      className="drag-ghost"
      style={{
        left,
        top,
        width,
        height,
        ['--card-accent' as string]: accent ?? 'transparent',
      }}
    >
      {accent && <span className="drag-ghost__accent" />}
      <div className="drag-ghost__title ream-truncate">
        {page.title || 'Untitled'}
      </div>
    </div>,
    document.body,
  );
}

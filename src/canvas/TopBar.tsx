/* The app's top bar: brand, canvas name + switcher, and primary canvas actions
   (new page, add column/row, import). */

import { useRef, useState } from 'react';
import type { Canvas } from '../db/types';
import { useDb } from '../db/DbProvider';
import { renameCanvas } from '../services/canvasService';
import { Button } from '../components/Button';
import { EditableText } from '../components/EditableText';
import { Icon } from '../components/Icon';
import { Popover } from '../components/Popover';
import './TopBar.css';

interface TopBarProps {
  canvas: Canvas | null;
  canvases: Canvas[];
  onSelectCanvas: (id: string) => void;
  onNewCanvas: () => void;
  onAddPage: () => void;
  onImport: () => void;
}

export function TopBar({
  canvas,
  canvases,
  onSelectCanvas,
  onNewCanvas,
  onAddPage,
  onImport,
}: TopBarProps) {
  const db = useDb();
  const [switcherOpen, setSwitcherOpen] = useState(false);
  const switcherRef = useRef<HTMLButtonElement>(null);

  return (
    <header className="topbar">
      <div className="topbar__brand">
        <Icon name="canvas" size={20} />
        <span className="topbar__brand-name">Ream</span>
      </div>

      <div className="topbar__divider" />

      <div className="topbar__canvas">
        {canvas && (
          <EditableText
            className="topbar__canvas-name"
            value={canvas.name}
            ariaLabel="Canvas name"
            onChange={(name) => renameCanvas(db, canvas.id, name)}
            placeholder="Untitled canvas"
          />
        )}
        <button
          ref={switcherRef}
          className="topbar__switcher"
          aria-label="Switch canvas"
          onClick={() => setSwitcherOpen((v) => !v)}
        >
          <Icon name="chevron-down" size={16} />
        </button>
        <Popover
          anchor={switcherRef.current}
          open={switcherOpen}
          onClose={() => setSwitcherOpen(false)}
        >
          <div className="canvas-list">
            <div className="popover__label">Canvases</div>
            {canvases.map((c) => (
              <button
                key={c.id}
                className={`canvas-list__item ${
                  c.id === canvas?.id ? 'is-active' : ''
                }`}
                onClick={() => {
                  onSelectCanvas(c.id);
                  setSwitcherOpen(false);
                }}
              >
                <Icon name="canvas" size={16} />
                <span className="ream-truncate">{c.name || 'Untitled'}</span>
                {c.id === canvas?.id && <Icon name="check" size={16} />}
              </button>
            ))}
            <button
              className="canvas-list__item canvas-list__new"
              onClick={() => {
                onNewCanvas();
                setSwitcherOpen(false);
              }}
            >
              <Icon name="plus" size={16} />
              <span>New canvas</span>
            </button>
          </div>
        </Popover>
      </div>

      <div className="ream-spacer" />

      <div className="topbar__actions">
        <Button icon="plus" variant="primary" size="sm" onClick={onAddPage}>
          Page
        </Button>
        <Button icon="file" variant="ghost" size="sm" onClick={onImport}>
          Import
        </Button>
      </div>
    </header>
  );
}

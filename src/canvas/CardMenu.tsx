/* Popover content for a page card: color, badges, and quick actions. */

import { useState } from 'react';
import type { Page } from '../db/types';
import { useDb } from '../db/DbProvider';
import { patchPage } from '../services/pageService';
import { movePageToLine, removePage } from '../services/columnService';
import { exportPageMarkdown } from '../export/exportMarkdown';
import { ColorSwatches } from '../components/ColorSwatches';
import { BadgeEditor } from '../components/BadgeEditor';
import { Button } from '../components/Button';

interface CardMenuProps {
  page: Page;
  /** True when the page shares a row and can be moved onto its own line. */
  canSplit?: boolean;
  onOpen: () => void;
  onClose: () => void;
}

export function CardMenu({ page, canSplit, onOpen, onClose }: CardMenuProps) {
  const db = useDb();
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  return (
    <div className="card-menu">
      <div className="popover__section">
        <div className="popover__label">Color</div>
        <ColorSwatches
          value={page.color}
          onChange={(color) => patchPage(db, page.id, { color })}
        />
      </div>

      <div className="popover__section">
        <div className="popover__label">Badges</div>
        <BadgeEditor
          badges={page.badges}
          onChange={(badges) => patchPage(db, page.id, { badges })}
        />
      </div>

      <div className="popover__section card-menu__actions">
        <Button icon="edit" size="sm" onClick={onOpen}>
          Open editor
        </Button>
        {canSplit && (
          <Button
            icon="row"
            size="sm"
            variant="ghost"
            onClick={async () => {
              await movePageToLine(db, page.canvasId, page.id, null);
              onClose();
            }}
          >
            Move to own line
          </Button>
        )}
        <Button
          icon="download"
          size="sm"
          variant="ghost"
          onClick={() => exportPageMarkdown(page)}
        >
          Export .md
        </Button>
        {confirmingDelete ? (
          <Button
            icon="trash"
            size="sm"
            variant="danger"
            onClick={async () => {
              await removePage(db, page.canvasId, page.id);
              onClose();
            }}
          >
            Confirm delete
          </Button>
        ) : (
          <Button
            icon="trash"
            size="sm"
            variant="ghost"
            onClick={() => setConfirmingDelete(true)}
          >
            Delete page
          </Button>
        )}
      </div>
    </div>
  );
}

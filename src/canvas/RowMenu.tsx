/* Popover content for a multi-page row: color, badges, export, ungroup. */

import type { Group, Page } from '../db/types';
import { useDb } from '../db/DbProvider';
import { patchGroup } from '../services/groupService';
import { ungroupRow } from '../services/columnService';
import { ColorSwatches } from '../components/ColorSwatches';
import { BadgeEditor } from '../components/BadgeEditor';
import { Button } from '../components/Button';

interface RowMenuProps {
  group: Group;
  pages: Page[];
  onClose: () => void;
}

export function RowMenu({ group, pages, onClose }: RowMenuProps) {
  const db = useDb();

  return (
    <div className="lane-menu">
      <div className="popover__section">
        <div className="popover__label">Highlight color</div>
        <ColorSwatches
          value={group.color}
          onChange={(color) => patchGroup(db, group.id, { color })}
        />
      </div>

      <div className="popover__section">
        <div className="popover__label">Badges</div>
        <BadgeEditor
          badges={group.badges}
          onChange={(badges) => patchGroup(db, group.id, { badges })}
        />
      </div>

      <div className="popover__section lane-menu__actions">
        <Button
          icon="download"
          size="sm"
          onClick={async () => {
            const { exportGroupPdf } = await import('../export/exportPdf');
            exportGroupPdf(group, pages);
            onClose();
          }}
        >
          Export PDF
        </Button>
        <Button
          icon="row"
          size="sm"
          variant="ghost"
          onClick={async () => {
            await ungroupRow(db, group.canvasId, group.id);
            onClose();
          }}
        >
          Split into separate lines
        </Button>
      </div>
    </div>
  );
}

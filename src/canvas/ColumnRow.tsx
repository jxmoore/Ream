/* One line in the column: a single bare page, or a multi-page row wrapped in a
   subtle (mostly transparent) container with an optional header and a trailing
   "+ Add page" slot. While dragging, a dashed slot opens at the merge index. */

import { useRef, useState } from 'react';
import type { Group, Page } from '../db/types';
import { useDb } from '../db/DbProvider';
import { patchGroup } from '../services/groupService';
import { resolveColor } from '../lib/constants';
import { PageCard } from './PageCard';
import { RowMenu } from './RowMenu';
import { EditableText } from '../components/EditableText';
import { BadgeList } from '../components/Badge';
import { IconButton } from '../components/Button';
import { Icon } from '../components/Icon';
import { Popover } from '../components/Popover';
import './ColumnRow.css';

interface ColumnRowProps {
  group: Group;
  pages: Page[];
  isDropTarget: boolean;
  dropIndex: number | null;
  draggingPageId: string | null;
  onOpenPage: (p: Page) => void;
  onStartDragPage: (e: React.PointerEvent, p: Page) => void;
  onStartRowDrag: (e: React.PointerEvent, g: Group) => void;
  onAddPage: (g: Group) => void;
}

export function ColumnRow({
  group,
  pages,
  isDropTarget,
  dropIndex,
  draggingPageId,
  onOpenPage,
  onStartDragPage,
  onStartRowDrag,
  onAddPage,
}: ColumnRowProps) {
  const db = useDb();
  const [menuOpen, setMenuOpen] = useState(false);
  const menuWrap = useRef<HTMLSpanElement>(null);

  const isMulti = pages.length > 1;
  const accent = resolveColor(group.color);

  // The dragged page stays in place (dimmed) so the rest of the column doesn't
  // collapse; the ghost follows the cursor. The merge slot opens at the drop
  // index counted among the non-dragged pages.
  const cards: React.ReactNode[] = [];
  const slot = <div className="row__slot" key="slot" aria-hidden="true" />;
  let nonDragged = 0;
  for (const page of pages) {
    const isDrag = page.id === draggingPageId;
    if (!isDrag && isDropTarget && dropIndex === nonDragged) cards.push(slot);
    cards.push(
      <PageCard
        key={page.id}
        page={page}
        dimmed={isDrag}
        canSplit={isMulti}
        onOpen={onOpenPage}
        onStartDrag={onStartDragPage}
      />,
    );
    if (!isDrag) nonDragged++;
  }
  if (isDropTarget && dropIndex === nonDragged) cards.push(slot);

  return (
    <section
      className={`row ${isMulti ? 'row--multi' : ''} ${
        isDropTarget ? 'row--target' : ''
      }`}
      data-row-id={group.id}
      style={{ ['--lane-accent' as string]: accent ?? 'var(--color-border-strong)' }}
    >
      {isMulti && (
        <header
          className="row__header"
          onPointerDown={(e) => onStartRowDrag(e, group)}
        >
          <span className="row__grip" aria-hidden="true">
            <Icon name="more" size={14} />
          </span>
          <EditableText
            className="row__name"
            value={group.name}
            ariaLabel="Row name"
            placeholder="Row"
            onChange={(name) => patchGroup(db, group.id, { name })}
          />
          {group.badges.length > 0 && <BadgeList badges={group.badges} />}
          <span className="row__count">{pages.length}</span>
          <span className="row__menu" ref={menuWrap}>
            <IconButton
              icon="more"
              label="Row options"
              size="sm"
              onPointerDown={(e) => e.stopPropagation()}
              onClick={() => setMenuOpen((v) => !v)}
            />
          </span>
        </header>
      )}

      <div className="row__body">
        {cards}
        {isMulti && (
          <button
            className="row__add"
            onClick={() => onAddPage(group)}
            aria-label="Add page to this row"
          >
            <Icon name="plus" size={18} />
            <span>Add</span>
          </button>
        )}
      </div>

      <Popover
        anchor={menuWrap.current}
        open={menuOpen}
        onClose={() => setMenuOpen(false)}
        align="end"
      >
        <RowMenu group={group} pages={pages} onClose={() => setMenuOpen(false)} />
      </Popover>
    </section>
  );
}

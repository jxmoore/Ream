/* A page rendered as a thumbnail card in the column. The card face shows a
   read-mode markdown preview; double-click opens the full-screen editor; the
   card is the drag handle (drag onto another page's side to make a row). */

import { memo, useRef, useState } from 'react';
import type { Page } from '../db/types';
import { resolveColor } from '../lib/constants';
import { MarkdownView } from '../components/MarkdownView';
import { BadgeList } from '../components/Badge';
import { IconButton } from '../components/Button';
import { Popover } from '../components/Popover';
import { CardMenu } from './CardMenu';
import './PageCard.css';

interface PageCardProps {
  page: Page;
  dimmed?: boolean;
  /** True when the page is in a multi-page row (so it can be split out). */
  canSplit?: boolean;
  onOpen: (page: Page) => void;
  onStartDrag: (e: React.PointerEvent, page: Page) => void;
}

function PageCardImpl({
  page,
  dimmed = false,
  canSplit = false,
  onOpen,
  onStartDrag,
}: PageCardProps) {
  const [menuOpen, setMenuOpen] = useState(false);
  const menuWrap = useRef<HTMLSpanElement>(null);
  const accent = resolveColor(page.color);

  return (
    <article
      className={`card ${dimmed ? 'card--dimmed' : ''}`}
      data-page-id={page.id}
      style={{ ['--card-accent' as string]: accent ?? 'transparent' }}
      onPointerDown={(e) => onStartDrag(e, page)}
      onDoubleClick={() => onOpen(page)}
    >
      {accent && <span className="card__accent" aria-hidden="true" />}

      <header className="card__header">
        <h3 className="card__title ream-truncate">
          {page.title || 'Untitled'}
        </h3>
        <span className="card__menu" ref={menuWrap}>
          <IconButton
            icon="more"
            label="Page options"
            size="sm"
            onPointerDown={(e) => e.stopPropagation()}
            onClick={() => setMenuOpen((v) => !v)}
          />
        </span>
      </header>

      {page.badges.length > 0 && (
        <div className="card__badges">
          <BadgeList badges={page.badges} />
        </div>
      )}

      <div className="card__body">
        {page.body.trim() ? (
          <MarkdownView markdown={page.body} />
        ) : (
          <span className="card__placeholder">Empty — double-click to write</span>
        )}
        <span className="card__fade" aria-hidden="true" />
      </div>

      <Popover
        anchor={menuWrap.current}
        open={menuOpen}
        onClose={() => setMenuOpen(false)}
        align="end"
      >
        <CardMenu
          page={page}
          canSplit={canSplit}
          onOpen={() => {
            setMenuOpen(false);
            onOpen(page);
          }}
          onClose={() => setMenuOpen(false)}
        />
      </Popover>
    </article>
  );
}

export const PageCard = memo(PageCardImpl);

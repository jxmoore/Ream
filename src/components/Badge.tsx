import { resolveColor } from '../lib/constants';
import type { Badge as BadgeType } from '../db/types';
import './Badge.css';

/** A read-only colored badge pill. */
export function Badge({ badge }: { badge: BadgeType }) {
  const hex = resolveColor(badge.color) ?? 'var(--hl-slate)';
  return (
    <span className="badge" style={{ ['--badge-color' as string]: hex }}>
      {badge.label}
    </span>
  );
}

export function BadgeList({ badges }: { badges: BadgeType[] }) {
  if (!badges.length) return null;
  return (
    <div className="badge-list">
      {badges.map((b, i) => (
        <Badge key={`${b.label}-${i}`} badge={b} />
      ))}
    </div>
  );
}

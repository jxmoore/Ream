import { useState } from 'react';
import { HIGHLIGHTS, resolveColor } from '../lib/constants';
import type { Badge } from '../db/types';
import { Icon } from './Icon';
import './BadgeEditor.css';

interface BadgeEditorProps {
  badges: Badge[];
  onChange: (badges: Badge[]) => void;
}

export function BadgeEditor({ badges, onChange }: BadgeEditorProps) {
  const [label, setLabel] = useState('');
  const [color, setColor] = useState(HIGHLIGHTS[6].key); // blue

  const add = () => {
    const trimmed = label.trim();
    if (!trimmed) return;
    onChange([...badges, { label: trimmed, color }]);
    setLabel('');
  };

  const remove = (index: number) => {
    onChange(badges.filter((_, i) => i !== index));
  };

  return (
    <div className="badge-editor">
      {badges.length > 0 && (
        <div className="badge-editor__list">
          {badges.map((b, i) => {
            const hex = resolveColor(b.color) ?? 'var(--hl-slate)';
            return (
              <span
                key={`${b.label}-${i}`}
                className="badge-editor__chip"
                style={{ ['--badge-color' as string]: hex }}
              >
                {b.label}
                <button
                  className="badge-editor__remove"
                  onClick={() => remove(i)}
                  aria-label={`Remove ${b.label}`}
                >
                  <Icon name="close" size={12} />
                </button>
              </span>
            );
          })}
        </div>
      )}

      <div className="badge-editor__add">
        <input
          className="badge-editor__input"
          placeholder="Add badge…"
          value={label}
          maxLength={24}
          onChange={(e) => setLabel(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault();
              add();
            }
          }}
        />
        <button
          className="badge-editor__add-btn"
          onClick={add}
          disabled={!label.trim()}
          aria-label="Add badge"
        >
          <Icon name="plus" size={16} />
        </button>
      </div>

      <div className="badge-editor__colors">
        {HIGHLIGHTS.map((h) => (
          <button
            key={h.key}
            className={`badge-editor__color ${
              color === h.key ? 'is-selected' : ''
            }`}
            style={{ background: h.hex }}
            onClick={() => setColor(h.key)}
            aria-label={`Badge color ${h.label}`}
            title={h.label}
          />
        ))}
      </div>
    </div>
  );
}

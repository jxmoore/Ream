import { HIGHLIGHTS } from '../lib/constants';
import { Icon } from './Icon';
import './ColorSwatches.css';

interface ColorSwatchesProps {
  value: string | null;
  onChange: (color: string | null) => void;
  /** Show a "no color" option. */
  allowNone?: boolean;
}

export function ColorSwatches({
  value,
  onChange,
  allowNone = true,
}: ColorSwatchesProps) {
  return (
    <div className="swatches">
      {allowNone && (
        <button
          className={`swatch swatch--none ${value === null ? 'is-selected' : ''}`}
          onClick={() => onChange(null)}
          aria-label="No color"
          title="No color"
        >
          <Icon name="close" size={14} />
        </button>
      )}
      {HIGHLIGHTS.map((h) => (
        <button
          key={h.key}
          className={`swatch ${value === h.key ? 'is-selected' : ''}`}
          style={{ background: h.hex }}
          onClick={() => onChange(h.key)}
          aria-label={h.label}
          title={h.label}
        >
          {value === h.key && <Icon name="check" size={14} />}
        </button>
      ))}
    </div>
  );
}

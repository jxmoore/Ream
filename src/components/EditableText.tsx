/* An input that looks like plain text until focused. Commits on debounce + blur.
   Reused for canvas titles and group/lane names. */

import { useEffect, useRef, useState } from 'react';
import { useDebouncedCallback } from '../hooks/useDebouncedCallback';
import './EditableText.css';

interface EditableTextProps {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  className?: string;
  maxLength?: number;
  ariaLabel?: string;
}

export function EditableText({
  value,
  onChange,
  placeholder,
  className = '',
  maxLength = 120,
  ariaLabel,
}: EditableTextProps) {
  const [draft, setDraft] = useState(value);
  const focused = useRef(false);
  const debounced = useDebouncedCallback(onChange, 400);

  // Keep in sync with external changes while not actively editing.
  useEffect(() => {
    if (!focused.current) setDraft(value);
  }, [value]);

  return (
    <input
      className={`editable-text ${className}`}
      value={draft}
      placeholder={placeholder}
      maxLength={maxLength}
      aria-label={ariaLabel}
      onChange={(e) => {
        setDraft(e.target.value);
        debounced.call(e.target.value);
      }}
      onFocus={() => (focused.current = true)}
      onBlur={() => {
        focused.current = false;
        debounced.flush();
      }}
      onPointerDown={(e) => e.stopPropagation()}
      onKeyDown={(e) => {
        if (e.key === 'Enter') (e.target as HTMLInputElement).blur();
      }}
    />
  );
}

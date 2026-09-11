/* A lightweight anchored popover. Renders into a portal, positions itself under
   (or above) the anchor element, and closes on outside click / Escape. */

import {
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { createPortal } from 'react-dom';
import './Popover.css';

interface PopoverProps {
  anchor: HTMLElement | null;
  open: boolean;
  onClose: () => void;
  children: ReactNode;
  align?: 'start' | 'center' | 'end';
}

export function Popover({
  anchor,
  open,
  onClose,
  children,
  align = 'start',
}: PopoverProps) {
  const ref = useRef<HTMLDivElement>(null);
  const [pos, setPos] = useState<{ top: number; left: number } | null>(null);

  useLayoutEffect(() => {
    if (!open || !anchor) return;
    const a = anchor.getBoundingClientRect();
    const el = ref.current;
    const width = el?.offsetWidth ?? 220;
    const height = el?.offsetHeight ?? 0;

    let left = a.left;
    if (align === 'center') left = a.left + a.width / 2 - width / 2;
    if (align === 'end') left = a.right - width;

    let top = a.bottom + 6;
    // Flip above if it would overflow the viewport bottom.
    if (top + height > window.innerHeight - 8) {
      top = Math.max(8, a.top - height - 6);
    }
    left = Math.min(Math.max(8, left), window.innerWidth - width - 8);
    setPos({ top, left });
  }, [open, anchor, align, children]);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: PointerEvent) => {
      if (
        ref.current &&
        !ref.current.contains(e.target as Node) &&
        anchor &&
        !anchor.contains(e.target as Node)
      ) {
        onClose();
      }
    };
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    window.addEventListener('pointerdown', onDown, true);
    window.addEventListener('keydown', onKey);
    return () => {
      window.removeEventListener('pointerdown', onDown, true);
      window.removeEventListener('keydown', onKey);
    };
  }, [open, anchor, onClose]);

  if (!open || !anchor) return null;

  return createPortal(
    <div
      ref={ref}
      className="popover"
      style={{
        top: pos?.top ?? -9999,
        left: pos?.left ?? -9999,
        visibility: pos ? 'visible' : 'hidden',
      }}
      role="dialog"
    >
      {children}
    </div>,
    document.body,
  );
}

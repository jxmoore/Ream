/* Lightweight read-mode markdown renderer. Kept separate from the heavy TipTap
   instance (§5: only instantiate TipTap for the focused page). */

import { useMemo } from 'react';
import { marked } from 'marked';
import DOMPurify from 'dompurify';
import './MarkdownView.css';

marked.setOptions({ gfm: true, breaks: false });

interface MarkdownViewProps {
  markdown: string;
  className?: string;
}

export function MarkdownView({ markdown, className = '' }: MarkdownViewProps) {
  const html = useMemo(() => {
    const raw = marked.parse(markdown ?? '', { async: false }) as string;
    return DOMPurify.sanitize(raw);
  }, [markdown]);

  return (
    <div
      className={`markdown ${className}`}
      dangerouslySetInnerHTML={{ __html: html }}
    />
  );
}

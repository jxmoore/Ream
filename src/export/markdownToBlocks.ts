/* Converts markdown into a flat list of render blocks for the PDF composer.
   Inline emphasis is flattened to plain text (jsPDF can't mix inline styles
   cheaply); structure (headings, lists, quotes, code) is preserved. */

import { marked, type Token } from 'marked';

export type Block =
  | { type: 'heading'; level: number; text: string }
  | { type: 'paragraph'; text: string }
  | { type: 'list-item'; text: string; depth: number; marker: string }
  | { type: 'code'; text: string }
  | { type: 'quote'; text: string }
  | { type: 'hr' };

// marked's inline token shapes are loose; treat them structurally.
interface InlineToken {
  type: string;
  text?: string;
  raw?: string;
  tokens?: InlineToken[];
}

function inlineText(tokens: InlineToken[] | undefined): string {
  if (!tokens) return '';
  return tokens
    .map((t) => {
      switch (t.type) {
        case 'text':
        case 'escape':
        case 'codespan':
          return t.text ?? '';
        case 'strong':
        case 'em':
        case 'del':
        case 'link':
          return inlineText(t.tokens) || t.text || '';
        case 'br':
          return '\n';
        default:
          return t.text ?? t.raw ?? '';
      }
    })
    .join('');
}

interface ListToken {
  ordered: boolean;
  start: number | '';
  items: { tokens: Token[] }[];
}

function walkList(list: ListToken, depth: number, out: Block[]): void {
  const start = typeof list.start === 'number' ? list.start : 1;
  list.items.forEach((item, i) => {
    const marker = list.ordered ? `${start + i}.` : '•';
    const parts: string[] = [];
    const nested: ListToken[] = [];
    for (const t of item.tokens as InlineToken[]) {
      if (t.type === 'list') {
        nested.push(t as unknown as ListToken);
      } else if (t.type === 'text' || t.type === 'paragraph') {
        parts.push(
          t.tokens ? inlineText(t.tokens) : t.text ?? '',
        );
      } else if (t.text) {
        parts.push(t.text);
      }
    }
    out.push({ type: 'list-item', text: parts.join(' ').trim(), depth, marker });
    nested.forEach((n) => walkList(n, depth + 1, out));
  });
}

export function markdownToBlocks(markdown: string): Block[] {
  const tokens = marked.lexer(markdown ?? '');
  const out: Block[] = [];

  for (const token of tokens as InlineToken[] & Token[]) {
    switch (token.type) {
      case 'heading':
        out.push({
          type: 'heading',
          level: (token as { depth: number }).depth,
          text: inlineText((token as InlineToken).tokens),
        });
        break;
      case 'paragraph':
        out.push({ type: 'paragraph', text: inlineText((token as InlineToken).tokens) });
        break;
      case 'list':
        walkList(token as unknown as ListToken, 0, out);
        break;
      case 'code':
        out.push({ type: 'code', text: (token as { text: string }).text });
        break;
      case 'blockquote':
        out.push({
          type: 'quote',
          text: inlineText((token as InlineToken).tokens),
        });
        break;
      case 'hr':
        out.push({ type: 'hr' });
        break;
      default:
        break;
    }
  }
  return out;
}

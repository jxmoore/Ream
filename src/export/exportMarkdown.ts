/* Phase 4: Page → markdown. Writes the stored `body` to a .md file. */

import type { Page } from '../db/types';
import { downloadBlob, sanitizeFilename } from '../lib/download';

export function exportPageMarkdown(page: Page): void {
  // Prefer an H1 title at the top if the body doesn't already start with one.
  const body = page.body ?? '';
  const hasLeadingHeading = /^\s*#\s/.test(body);
  const content = hasLeadingHeading
    ? body
    : `# ${page.title || 'Untitled'}\n\n${body}`;

  const blob = new Blob([content], { type: 'text/markdown;charset=utf-8' });
  downloadBlob(blob, `${sanitizeFilename(page.title)}.md`);
}

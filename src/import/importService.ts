/* Phase 3 (free tier): turn dropped files into pages, appended to the column.
   - .md / .txt: one page line, text used verbatim (already the storage format).
   - .pdf: extract text per page in the worker. A single page → one line; a
     multi-page PDF → a named row of page-cards (basic, lossy; OCR/LLM cleanup
     is the premium "smart import" in Phase 7). */

import type { ReamDatabase } from '../db/database';
import { appendRow } from '../services/columnService';
import { extractPdfText } from './pdfClient';

export type NotifyLevel = 'info' | 'success' | 'error';

export interface ImportContext {
  db: ReamDatabase;
  canvasId: string;
  notify: (message: string, level?: NotifyLevel) => void;
}

const SUPPORTED_EXT = ['md', 'markdown', 'txt', 'text', 'pdf'];

function extOf(name: string): string {
  return name.split('.').pop()?.toLowerCase() ?? '';
}

function baseName(name: string): string {
  return name.replace(/\.[^.]+$/, '').trim() || 'Imported note';
}

/** Use a leading markdown H1 as the title if present, else the filename. */
function deriveTitle(markdown: string, fallback: string): string {
  const match = markdown.match(/^\s*#\s+(.+)$/m);
  return match ? match[1].trim() : fallback;
}

async function importTextFile(file: File, ctx: ImportContext): Promise<void> {
  const text = await file.text();
  const title = deriveTitle(text, baseName(file.name));
  await appendRow(ctx.db, ctx.canvasId, [{ title, body: text }]);
  ctx.notify(`Imported “${title}”`, 'success');
}

async function importPdfFile(file: File, ctx: ImportContext): Promise<void> {
  ctx.notify(`Reading ${file.name}…`, 'info');
  const buffer = await file.arrayBuffer();
  const rawPages = await extractPdfText(buffer);
  const pages = rawPages.map((t) => t.trim()).filter((t) => t.length > 0);

  if (pages.length === 0) {
    ctx.notify(
      `No selectable text found in ${file.name}. It may be scanned — OCR is a premium feature.`,
      'error',
    );
    return;
  }

  const name = baseName(file.name);

  if (pages.length === 1) {
    await appendRow(ctx.db, ctx.canvasId, [{ title: name, body: pages[0] }]);
    ctx.notify(`Imported “${name}”`, 'success');
    return;
  }

  await appendRow(
    ctx.db,
    ctx.canvasId,
    pages.map((body, i) => ({ title: `${name} — page ${i + 1}`, body })),
    name,
  );
  ctx.notify(`Imported ${pages.length} pages into “${name}”`, 'success');
}

async function importOne(file: File, ctx: ImportContext): Promise<void> {
  const ext = extOf(file.name);
  try {
    if (ext === 'pdf') {
      await importPdfFile(file, ctx);
    } else if (SUPPORTED_EXT.includes(ext) || file.type.startsWith('text/')) {
      await importTextFile(file, ctx);
    } else {
      ctx.notify(`Unsupported file type: ${file.name}`, 'error');
    }
  } catch (err) {
    console.error('Import failed', file.name, err);
    ctx.notify(`Couldn’t import ${file.name}`, 'error');
  }
}

/** Import a batch of dropped files, appending each to the column. */
export async function importFiles(
  files: FileList | File[],
  ctx: ImportContext,
): Promise<void> {
  // Sequential so rows append in a predictable order.
  for (const file of Array.from(files)) {
    await importOne(file, ctx);
  }
}

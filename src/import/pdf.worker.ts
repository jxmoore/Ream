/* Web Worker: extracts text from a PDF with pdf.js, off the main thread (§3
   constraint #4). Returns one text string per PDF page so the importer can
   decide how to split them into page-cards. */

/// <reference lib="webworker" />
import * as pdfjs from 'pdfjs-dist';
import workerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url';

// pdf.js needs a worker for its own parsing. Pointing at the bundled worker
// keeps the heavy parsing fully off the UI thread.
pdfjs.GlobalWorkerOptions.workerSrc = workerUrl;

export interface PdfExtractRequest {
  id: string;
  buffer: ArrayBuffer;
}

export type PdfExtractResponse =
  | { id: string; type: 'progress'; page: number; total: number }
  | { id: string; type: 'done'; pages: string[] }
  | { id: string; type: 'error'; message: string };

interface TextItemLike {
  str?: string;
  hasEOL?: boolean;
}

function normalize(text: string): string {
  return text
    .replace(/[ \t]+\n/g, '\n')
    .replace(/\n{3,}/g, '\n\n')
    .trim();
}

async function extract(id: string, buffer: ArrayBuffer): Promise<void> {
  const doc = await pdfjs.getDocument({ data: new Uint8Array(buffer) }).promise;
  const pages: string[] = [];

  for (let n = 1; n <= doc.numPages; n++) {
    const page = await doc.getPage(n);
    const content = await page.getTextContent();
    let text = '';
    for (const item of content.items as TextItemLike[]) {
      text += item.str ?? '';
      if (item.hasEOL) text += '\n';
    }
    pages.push(normalize(text));
    page.cleanup();
    post({ id, type: 'progress', page: n, total: doc.numPages });
  }

  await doc.destroy();
  post({ id, type: 'done', pages });
}

function post(msg: PdfExtractResponse): void {
  (self as DedicatedWorkerGlobalScope).postMessage(msg);
}

self.onmessage = (e: MessageEvent<PdfExtractRequest>) => {
  const { id, buffer } = e.data;
  extract(id, buffer).catch((err) => {
    post({ id, type: 'error', message: err?.message ?? String(err) });
  });
};

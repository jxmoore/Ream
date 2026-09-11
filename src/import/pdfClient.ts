/* Main-thread client for the PDF worker. Manages the worker lifecycle and
   correlates request/response by id so multiple imports can run concurrently. */

import { createId } from '../lib/id';
import type {
  PdfExtractRequest,
  PdfExtractResponse,
} from './pdf.worker';

type ProgressFn = (page: number, total: number) => void;

let worker: Worker | null = null;

function getWorker(): Worker {
  if (!worker) {
    worker = new Worker(new URL('./pdf.worker.ts', import.meta.url), {
      type: 'module',
    });
  }
  return worker;
}

/** Extract one text string per PDF page. */
export function extractPdfText(
  buffer: ArrayBuffer,
  onProgress?: ProgressFn,
): Promise<string[]> {
  const w = getWorker();
  const id = createId();

  return new Promise((resolve, reject) => {
    const handle = (e: MessageEvent<PdfExtractResponse>) => {
      const msg = e.data;
      if (msg.id !== id) return;
      if (msg.type === 'progress') {
        onProgress?.(msg.page, msg.total);
      } else if (msg.type === 'done') {
        w.removeEventListener('message', handle);
        resolve(msg.pages);
      } else if (msg.type === 'error') {
        w.removeEventListener('message', handle);
        reject(new Error(msg.message));
      }
    };
    w.addEventListener('message', handle);

    const req: PdfExtractRequest = { id, buffer };
    // Transfer the buffer to avoid a copy.
    w.postMessage(req, [buffer]);
  });
}

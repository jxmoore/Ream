/* Phase 4: Row/column → PDF. Composes a group's pages into one document. */

import { jsPDF } from 'jspdf';
import type { Group, Page } from '../db/types';
import { markdownToBlocks, type Block } from './markdownToBlocks';
import { resolveColor } from '../lib/constants';
import { sanitizeFilename } from '../lib/download';

const MARGIN = 56;
const FONT = 'helvetica';

/** Lays out blocks top-to-bottom with automatic page breaks. */
class PdfWriter {
  private doc: jsPDF;
  private y: number;
  private readonly pageW: number;
  private readonly pageH: number;
  private readonly contentW: number;

  constructor() {
    this.doc = new jsPDF({ unit: 'pt', format: 'a4' });
    this.pageW = this.doc.internal.pageSize.getWidth();
    this.pageH = this.doc.internal.pageSize.getHeight();
    this.contentW = this.pageW - MARGIN * 2;
    this.y = MARGIN;
  }

  private ensure(height: number): void {
    if (this.y + height > this.pageH - MARGIN) {
      this.doc.addPage();
      this.y = MARGIN;
    }
  }

  private lines(
    text: string,
    fontSize: number,
    style: 'normal' | 'bold' | 'italic',
    indent = 0,
    font = FONT,
  ): void {
    if (!text) {
      this.y += fontSize * 0.6;
      return;
    }
    this.doc.setFont(font, style);
    this.doc.setFontSize(fontSize);
    const width = this.contentW - indent;
    const wrapped = this.doc.splitTextToSize(text, width) as string[];
    const lineH = fontSize * 1.4;
    for (const line of wrapped) {
      this.ensure(lineH);
      this.doc.text(line, MARGIN + indent, this.y + fontSize);
      this.y += lineH;
    }
  }

  gap(amount: number): void {
    this.y += amount;
  }

  rule(color = '#d0d0d8'): void {
    this.ensure(12);
    this.doc.setDrawColor(color);
    this.doc.setLineWidth(0.75);
    this.doc.line(MARGIN, this.y + 4, this.pageW - MARGIN, this.y + 4);
    this.y += 12;
  }

  /** A prominent document title with an accent rule. */
  documentTitle(text: string, accent: string | null): void {
    this.doc.setTextColor('#15151a');
    this.lines(text, 24, 'bold');
    this.doc.setDrawColor(accent ?? '#6d5cff');
    this.doc.setLineWidth(2.5);
    this.doc.line(MARGIN, this.y + 2, MARGIN + 64, this.y + 2);
    this.y += 18;
  }

  pageTitle(text: string): void {
    this.gap(6);
    this.doc.setTextColor('#1f1f29');
    this.lines(text, 16, 'bold');
    this.gap(2);
  }

  block(b: Block): void {
    this.doc.setTextColor('#26262f');
    switch (b.type) {
      case 'heading': {
        this.gap(6);
        const size = b.level === 1 ? 17 : b.level === 2 ? 14 : 12.5;
        this.lines(b.text, size, 'bold');
        this.gap(2);
        break;
      }
      case 'paragraph':
        this.lines(b.text, 11, 'normal');
        this.gap(5);
        break;
      case 'list-item':
        this.lines(
          `${b.marker}  ${b.text}`,
          11,
          'normal',
          16 + b.depth * 16,
        );
        break;
      case 'quote':
        this.doc.setTextColor('#5a5a6a');
        this.lines(b.text, 11, 'italic', 14);
        this.gap(5);
        break;
      case 'code': {
        this.gap(2);
        this.doc.setTextColor('#33333f');
        for (const line of b.text.split('\n')) {
          this.lines(line || ' ', 9.5, 'normal', 12, 'courier');
        }
        this.gap(6);
        break;
      }
      case 'hr':
        this.rule();
        break;
    }
  }

  save(filename: string): void {
    // Footer page numbers.
    const total = this.doc.getNumberOfPages();
    for (let p = 1; p <= total; p++) {
      this.doc.setPage(p);
      this.doc.setFont(FONT, 'normal');
      this.doc.setFontSize(9);
      this.doc.setTextColor('#9a9aa8');
      this.doc.text(
        `${p} / ${total}`,
        this.pageW / 2,
        this.pageH - 28,
        { align: 'center' },
      );
    }
    this.doc.save(filename);
  }
}

export function exportGroupPdf(group: Group, pages: Page[]): void {
  const writer = new PdfWriter();
  const accent = resolveColor(group.color);

  writer.documentTitle(group.name || 'Untitled group', accent);

  pages.forEach((page, i) => {
    if (i > 0) writer.rule('#e2e2ea');
    writer.pageTitle(page.title || 'Untitled');
    for (const block of markdownToBlocks(page.body)) {
      writer.block(block);
    }
    writer.gap(8);
  });

  if (pages.length === 0) {
    writer.block({ type: 'paragraph', text: 'This group has no pages yet.' });
  }

  const kind = group.type === 'column' ? 'column' : 'row';
  writer.save(`${sanitizeFilename(group.name, kind)}.pdf`);
}

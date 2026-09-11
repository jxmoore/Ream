/* The board: a vertical, ordered column of rows. Owns reactive page/row data,
   the drag controller, the editor overlay, and import. Rendering of rows/cards/
   chrome is delegated; this file is orchestration. */

import {
  Fragment,
  lazy,
  Suspense,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import type { Page, Group } from '../db/types';
import { useDb } from '../db/DbProvider';
import { usePages, useGroups, useCanvases } from '../hooks/useCanvasData';
import { useColumnDrag } from './useColumnDrag';
import { useFileDrop } from '../import/useFileDrop';
import { importFiles } from '../import/importService';
import {
  appendPageLine,
  addPageToRow,
  removePage,
  normalizeColumn,
} from '../services/columnService';
import { createCanvas } from '../services/canvasService';
import { useToast } from '../components/Toast';
import { TopBar } from './TopBar';
import { ColumnRow } from './ColumnRow';
import { DragLayer } from './DragLayer';
import './Board.css';

const PageEditor = lazy(() =>
  import('../editor/PageEditor').then((m) => ({ default: m.PageEditor })),
);

interface BoardProps {
  canvasId: string;
  onSelectCanvas: (id: string) => void;
}

const ACCEPT = '.md,.markdown,.txt,.text,.pdf';

export function Board({ canvasId, onSelectCanvas }: BoardProps) {
  const db = useDb();
  const { notify } = useToast();

  const canvases = useCanvases();
  const canvas = useMemo(
    () => canvases.find((c) => c.id === canvasId) ?? null,
    [canvases, canvasId],
  );
  const pages = usePages(canvasId);
  const groups = useGroups(canvasId);

  const { dragState, startPageDrag, startRowDrag } = useColumnDrag(db, canvasId);
  const fileInput = useRef<HTMLInputElement>(null);
  const scrollRef = useRef<HTMLDivElement>(null);
  // Page just added that should be scrolled into view (only if off-screen).
  const pendingScroll = useRef<string | null>(null);

  const [openPageId, setOpenPageId] = useState<string | null>(null);
  const [createdId, setCreatedId] = useState<string | null>(null);

  const openNewPage = useCallback((id: string) => {
    setOpenPageId(id);
    setCreatedId(id);
  }, []);
  const openExistingPage = useCallback((id: string) => {
    setOpenPageId(id);
    setCreatedId(null);
  }, []);
  const closeEditor = useCallback(() => {
    setOpenPageId(null);
    setCreatedId(null);
  }, []);

  // Wrap any orphan pages (no row) into their own rows on load.
  useEffect(() => {
    void normalizeColumn(db, canvasId);
  }, [db, canvasId]);

  // Build column lines: rows in order, each with its members sorted.
  const lines = useMemo(() => {
    const byGroup = new Map<string, Page[]>();
    for (const p of pages) {
      if (!p.groupId) continue;
      const list = byGroup.get(p.groupId) ?? [];
      list.push(p);
      byGroup.set(p.groupId, list);
    }
    for (const list of byGroup.values()) list.sort((a, b) => a.order - b.order);
    return groups.map((group) => ({
      group,
      pages: byGroup.get(group.id) ?? [],
    }));
  }, [pages, groups]);

  // After a page is added and rendered, bring it into view only if it isn't
  // already visible — `block: 'nearest'` is a no-op when it's on screen.
  useEffect(() => {
    const id = pendingScroll.current;
    if (!id) return;
    const el = document.querySelector(`[data-page-id="${id}"]`);
    if (el) {
      pendingScroll.current = null;
      el.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    }
  }, [lines]);

  const openPage = useMemo(
    () => pages.find((p) => p.id === openPageId) ?? null,
    [pages, openPageId],
  );
  const draggedPage = useMemo(
    () =>
      dragState?.kind === 'page'
        ? pages.find((p) => p.id === dragState.id)
        : undefined,
    [pages, dragState],
  );

  /* --- Actions ---------------------------------------------------------- */

  const handleAddPage = useCallback(async () => {
    const page = await appendPageLine(db, canvasId);
    pendingScroll.current = page.id;
    openNewPage(page.id);
  }, [db, canvasId, openNewPage]);

  const handleAddPageToRow = useCallback(
    async (group: Group) => {
      const page = await addPageToRow(db, canvasId, group.id);
      pendingScroll.current = page.id;
      openNewPage(page.id);
    },
    [db, canvasId, openNewPage],
  );

  const handleNewCanvas = useCallback(async () => {
    const created = await createCanvas(db, 'Untitled canvas');
    onSelectCanvas(created.id);
  }, [db, onSelectCanvas]);

  const runImport = useCallback(
    (files: FileList | File[]) => importFiles(files, { db, canvasId, notify }),
    [db, canvasId, notify],
  );

  const { isDraggingFile, dropHandlers } = useFileDrop((files) =>
    runImport(files),
  );

  const onPickFiles = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      if (e.target.files?.length) runImport(e.target.files);
      e.target.value = '';
    },
    [runImport],
  );

  /* --- Drop indicators -------------------------------------------------- */

  const drop = dragState?.drop ?? null;
  const dropNewLine = drop?.kind === 'new-line' ? drop : null;
  const dropIntoRow = drop?.kind === 'into-row' ? drop : null;
  const draggingPageId = dragState?.kind === 'page' ? dragState.id : null;

  const bar = <div className="column__bar" key="bar" aria-hidden="true" />;

  return (
    <div className="board-root">
      <TopBar
        canvas={canvas}
        canvases={canvases}
        onSelectCanvas={onSelectCanvas}
        onNewCanvas={handleNewCanvas}
        onAddPage={handleAddPage}
        onImport={() => fileInput.current?.click()}
      />

      <div
        ref={scrollRef}
        className={`board-scroll ${isDraggingFile ? 'is-file-target' : ''}`}
        {...dropHandlers}
      >
        <div className="column">
          {lines.map((line) => (
            <Fragment key={line.group.id}>
              {dropNewLine?.beforeGroupId === line.group.id && bar}
              <ColumnRow
                group={line.group}
                pages={line.pages}
                isDropTarget={dropIntoRow?.groupId === line.group.id}
                dropIndex={
                  dropIntoRow?.groupId === line.group.id ? dropIntoRow.index : null
                }
                draggingPageId={draggingPageId}
                onOpenPage={(p) => openExistingPage(p.id)}
                onStartDragPage={startPageDrag}
                onStartRowDrag={startRowDrag}
                onAddPage={handleAddPageToRow}
              />
            </Fragment>
          ))}
          {dropNewLine?.beforeGroupId === null && bar}

          <button className="column__add" onClick={handleAddPage}>
            <span>+ Add page</span>
          </button>
        </div>

        {isDraggingFile && (
          <div className="board-drop-overlay">
            <div className="board-drop-card">Drop files to add pages</div>
          </div>
        )}
      </div>

      <input
        ref={fileInput}
        type="file"
        accept={ACCEPT}
        multiple
        className="ream-visually-hidden"
        onChange={onPickFiles}
      />

      {dragState?.kind === 'page' && draggedPage && (
        <DragLayer
          page={draggedPage}
          left={dragState.left}
          top={dragState.top}
          width={dragState.width}
          height={dragState.height}
        />
      )}

      {openPage && (
        <Suspense fallback={null}>
          <PageEditor
            key={openPage.id}
            page={openPage}
            isNew={openPage.id === createdId}
            onClose={closeEditor}
            onDelete={async (p) => {
              await removePage(db, canvasId, p.id);
              closeEditor();
            }}
          />
        </Suspense>
      )}
    </div>
  );
}

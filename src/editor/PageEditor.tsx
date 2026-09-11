/* Full-screen editor for a focused page. Mounts a TipTap instance (heavy) only
   while a page is open, persists markdown on a debounce, and flushes on close. */

import { useCallback, useEffect, useRef, useState } from 'react';
import { useEditor, EditorContent } from '@tiptap/react';
import type { Page } from '../db/types';
import { useDb } from '../db/DbProvider';
import { patchPage } from '../services/pageService';
import { editorExtensions } from './extensions';
import { EditorToolbar } from './EditorToolbar';
import { IconButton, Button } from '../components/Button';
import { useDebouncedCallback } from '../hooks/useDebouncedCallback';
import { exportPageMarkdown } from '../export/exportMarkdown';
import './PageEditor.css';

type SaveStatus = 'saved' | 'pending';

interface PageEditorProps {
  page: Page;
  /** True when this page was just created by opening the editor. */
  isNew?: boolean;
  onClose: () => void;
  onDelete: (page: Page) => void;
}

export function PageEditor({ page, isNew = false, onClose, onDelete }: PageEditorProps) {
  const db = useDb();
  const [title, setTitle] = useState(page.title);
  const [status, setStatus] = useState<SaveStatus>('saved');
  const latest = useRef({ title: page.title, body: page.body });
  // Whether the user has actually edited anything since opening.
  const touched = useRef(false);

  const persist = useCallback(async () => {
    await patchPage(db, page.id, {
      title: latest.current.title.trim() || 'Untitled',
      body: latest.current.body,
    });
    setStatus('saved');
  }, [db, page.id]);

  const debounced = useDebouncedCallback(persist, 500);

  const editor = useEditor({
    extensions: editorExtensions,
    content: page.body,
    autofocus: 'end',
    onUpdate: ({ editor }) => {
      latest.current.body = editor.storage.markdown.getMarkdown();
      touched.current = true;
      setStatus('pending');
      debounced.call();
    },
  });

  // Flush any pending save when the editor closes/unmounts (unless discarded).
  useEffect(() => {
    return () => {
      debounced.flush();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleTitle = (value: string) => {
    setTitle(value);
    latest.current.title = value;
    touched.current = true;
    setStatus('pending');
    debounced.call();
  };

  const handleClose = () => {
    // A just-created page left completely untouched is discarded, not kept.
    if (isNew && !touched.current) {
      debounced.cancel();
      onDelete(page);
      return;
    }
    debounced.flush();
    onClose();
  };

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') handleClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="page-editor">
      <header className="page-editor__bar">
        <IconButton icon="fit" label="Back to canvas" onClick={handleClose} />
        <input
          className="page-editor__title"
          value={title}
          placeholder="Untitled"
          onChange={(e) => handleTitle(e.target.value)}
        />
        <span className="page-editor__status" data-status={status}>
          {status === 'saved' ? 'Saved' : 'Saving…'}
        </span>
        <div className="ream-spacer" />
        <Button
          icon="download"
          variant="ghost"
          size="sm"
          onClick={() => exportPageMarkdown({ ...page, title, body: latest.current.body })}
        >
          .md
        </Button>
        <IconButton
          icon="trash"
          label="Delete page"
          variant="danger"
          onClick={() => onDelete(page)}
        />
        <IconButton icon="close" label="Close editor" onClick={handleClose} />
      </header>

      {editor && <EditorToolbar editor={editor} />}

      <div className="page-editor__scroll">
        <div className="page-editor__sheet">
          <EditorContent editor={editor} className="markdown" />
        </div>
      </div>
    </div>
  );
}

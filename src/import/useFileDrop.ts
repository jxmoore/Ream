/* Tracks file drag-over state and reports dropped files (column mode appends
   them, so the drop position is irrelevant). */

import { useCallback, useRef, useState } from 'react';

interface FileDropHandlers {
  onDragEnter: (e: React.DragEvent) => void;
  onDragOver: (e: React.DragEvent) => void;
  onDragLeave: (e: React.DragEvent) => void;
  onDrop: (e: React.DragEvent) => void;
}

export function useFileDrop(
  onFiles: (files: FileList) => void,
): { isDraggingFile: boolean; dropHandlers: FileDropHandlers } {
  const [isDraggingFile, setDraggingFile] = useState(false);
  const depth = useRef(0);

  const hasFiles = (e: React.DragEvent) =>
    Array.from(e.dataTransfer.types).includes('Files');

  const onDragEnter = useCallback((e: React.DragEvent) => {
    if (!hasFiles(e)) return;
    e.preventDefault();
    depth.current += 1;
    setDraggingFile(true);
  }, []);

  const onDragOver = useCallback((e: React.DragEvent) => {
    if (!hasFiles(e)) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = 'copy';
  }, []);

  const onDragLeave = useCallback((e: React.DragEvent) => {
    if (!hasFiles(e)) return;
    depth.current -= 1;
    if (depth.current <= 0) {
      depth.current = 0;
      setDraggingFile(false);
    }
  }, []);

  const onDrop = useCallback(
    (e: React.DragEvent) => {
      if (!hasFiles(e)) return;
      e.preventDefault();
      depth.current = 0;
      setDraggingFile(false);
      if (e.dataTransfer.files.length > 0) onFiles(e.dataTransfer.files);
    },
    [onFiles],
  );

  return {
    isDraggingFile,
    dropHandlers: { onDragEnter, onDragOver, onDragLeave, onDrop },
  };
}

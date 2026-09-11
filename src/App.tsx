/* App shell: gates rendering on database readiness, then mounts the canvas for
   the selected board. Auth, sync, subscriptions, and the native shell are
   deferred (later phases). */

import { useEffect, useState } from 'react';
import { useDatabaseContext } from './db/DbProvider';
import { Board } from './canvas/Board';
import './App.css';

export function App() {
  const { status, error, initialCanvasId } = useDatabaseContext();
  const [canvasId, setCanvasId] = useState<string | null>(null);

  useEffect(() => {
    if (initialCanvasId && !canvasId) setCanvasId(initialCanvasId);
  }, [initialCanvasId, canvasId]);

  if (status === 'loading') {
    return (
      <div className="app-splash">
        <div className="app-splash__logo">Ream</div>
        <div className="app-splash__spinner" />
      </div>
    );
  }

  if (status === 'error') {
    return (
      <div className="app-splash">
        <div className="app-splash__logo">Ream</div>
        <p className="app-splash__error">
          Couldn’t open the local database.
          <br />
          {error?.message}
        </p>
      </div>
    );
  }

  if (!canvasId) return null;

  return <Board canvasId={canvasId} onSelectCanvas={setCanvasId} />;
}

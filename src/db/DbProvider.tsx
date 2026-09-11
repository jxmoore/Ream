/* Initializes the RxDB database + default canvas once, and exposes them to the
   tree via context. Renders a loading / error state until the DB is ready. */

import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { getDatabase, type ReamDatabase } from './database';
import { seedDefaultCanvas } from './seed';

type Status = 'loading' | 'ready' | 'error';

interface DbContextValue {
  db: ReamDatabase | null;
  status: Status;
  error: Error | null;
  /** The canvas opened on startup. */
  initialCanvasId: string | null;
}

const DbContext = createContext<DbContextValue | null>(null);

export function DbProvider({ children }: { children: ReactNode }) {
  const [db, setDb] = useState<ReamDatabase | null>(null);
  const [initialCanvasId, setInitialCanvasId] = useState<string | null>(null);
  const [status, setStatus] = useState<Status>('loading');
  const [error, setError] = useState<Error | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const database = await getDatabase();
        const canvasId = await seedDefaultCanvas(database);
        if (cancelled) return;
        setDb(database);
        setInitialCanvasId(canvasId);
        setStatus('ready');
      } catch (err) {
        if (cancelled) return;
        console.error('Failed to initialize Ream database', err);
        setError(err as Error);
        setStatus('error');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const value = useMemo<DbContextValue>(
    () => ({ db, status, error, initialCanvasId }),
    [db, status, error, initialCanvasId],
  );

  return <DbContext.Provider value={value}>{children}</DbContext.Provider>;
}

export function useDatabaseContext(): DbContextValue {
  const ctx = useContext(DbContext);
  if (!ctx) throw new Error('useDatabaseContext must be used within DbProvider');
  return ctx;
}

/** Returns the ready database. Only call from subtrees rendered after ready. */
export function useDb(): ReamDatabase {
  const { db } = useDatabaseContext();
  if (!db) throw new Error('Database accessed before it was ready');
  return db;
}

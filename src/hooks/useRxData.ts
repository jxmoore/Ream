/* Bridges an RxDB reactive query to React state. Returns plain JSON documents
   that get a fresh reference on every emit, so React re-renders on any change —
   including changes that arrive via sync later. */

import { useEffect, useState, type DependencyList } from 'react';
import type { RxQuery, RxDocument } from 'rxdb';
import { useDatabaseContext } from '../db/DbProvider';
import type { ReamDatabase } from '../db/database';

type AnyQuery<T> =
  | RxQuery<T, RxDocument<T>[]>
  | RxQuery<T, RxDocument<T> | null>;

/**
 * @param build  Builds the query from the database. Return null to skip.
 * @param deps   Re-run when these change (the query is rebuilt + resubscribed).
 */
export function useRxData<T>(
  build: (db: ReamDatabase) => AnyQuery<T> | null,
  deps: DependencyList,
): T[] {
  const { db } = useDatabaseContext();
  const [data, setData] = useState<T[]>([]);

  useEffect(() => {
    if (!db) return;
    const query = build(db);
    if (!query) {
      setData([]);
      return;
    }
    // The two query shapes emit different result types; normalize to an array.
    type Emission = RxDocument<T>[] | RxDocument<T> | null;
    const observable = query.$ as unknown as {
      subscribe: (next: (r: Emission) => void) => { unsubscribe: () => void };
    };
    const sub = observable.subscribe((result) => {
      const docs = Array.isArray(result) ? result : result ? [result] : [];
      setData(docs.map((d) => d.toJSON() as T));
    });
    return () => sub.unsubscribe();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [db, ...deps]);

  return data;
}

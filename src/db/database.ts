/* ===========================================================================
   RxDB database singleton.

   - Dexie (IndexedDB) storage — the free, offline-first adapter (§2). Swapping
     to the SQLite premium plugin later is a one-line change here.
   - Dev-mode plugin + AJV schema validation are enabled only in dev (§6 Phase 0).
   - A preSave/preInsert hook keeps `updatedAt` fresh on every write so the value
     is ready to map onto Supabase's `_modified` column when sync lands.
   =========================================================================== */

import { createRxDatabase, addRxPlugin, type RxDatabase } from 'rxdb';
import { getRxStorageDexie } from 'rxdb/plugins/storage-dexie';
import {
  canvasSchema,
  pageSchema,
  groupSchema,
  type ReamCollections,
} from './schemas';

export type ReamDatabase = RxDatabase<ReamCollections>;

const DB_NAME = 'ream';
const isDev = import.meta.env.DEV;

let pluginsRegistered = false;

async function registerPlugins(): Promise<void> {
  if (pluginsRegistered) return;
  pluginsRegistered = true;

  if (isDev) {
    // Catches schema mistakes, illegal writes, and bad queries early.
    const { RxDBDevModePlugin, disableWarnings } = await import(
      'rxdb/plugins/dev-mode'
    );
    // Silence the verbose "you are in dev-mode" console banner; keep the checks.
    disableWarnings();
    addRxPlugin(RxDBDevModePlugin);
  }
}

async function buildStorage() {
  const dexie = getRxStorageDexie();
  if (isDev) {
    // AJV validation wraps the storage in dev so invalid documents throw.
    const { wrappedValidateAjvStorage } = await import(
      'rxdb/plugins/validate-ajv'
    );
    return wrappedValidateAjvStorage({ storage: dexie });
  }
  return dexie;
}

/** Bumps updatedAt on every write so modification time is always accurate. */
function attachTimestampHooks(db: ReamDatabase): void {
  for (const name of ['canvases', 'pages', 'groups'] as const) {
    const collection = db[name];
    collection.preSave((data) => {
      data.updatedAt = Date.now();
    }, false);
    collection.preInsert((data) => {
      const now = Date.now();
      if (!data.createdAt) data.createdAt = now;
      data.updatedAt = now;
    }, false);
  }
}

let dbPromise: Promise<ReamDatabase> | null = null;

async function createDatabase(): Promise<ReamDatabase> {
  await registerPlugins();
  const storage = await buildStorage();

  const db = await createRxDatabase<ReamCollections>({
    name: DB_NAME,
    storage,
    multiInstance: true, // sync across browser tabs via BroadcastChannel
    eventReduce: true,
    // Avoids "database already exists" errors across HMR reloads in dev.
    ignoreDuplicate: isDev,
  });

  await db.addCollections({
    canvases: { schema: canvasSchema },
    pages: { schema: pageSchema },
    groups: { schema: groupSchema },
  });

  attachTimestampHooks(db);

  return db;
}

/** Returns the shared database, creating it on first call. */
export function getDatabase(): Promise<ReamDatabase> {
  if (!dbPromise) {
    dbPromise = createDatabase().catch((err) => {
      // Reset so a later retry can rebuild after a transient failure.
      dbPromise = null;
      throw err;
    });
  }
  return dbPromise;
}

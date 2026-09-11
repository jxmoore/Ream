/* ===========================================================================
   RxDB JSON schemas for the four §4 collections.

   Hard constraints honored:
   - String primary keys everywhere (nanoid). Never auto-increment.
   - Soft-delete only is handled by RxDB's built-in `_deleted` flag.
   - `updatedAt` is bumped on every write (see database.ts preSave hook) so it
     can map to the Supabase `_modified` column when sync lands.
   =========================================================================== */

import type {
  RxJsonSchema,
  RxCollection,
  RxDocument,
} from 'rxdb';
import type { Canvas, Page, Group, Badge } from './types';

const badgeSchema = {
  type: 'object',
  properties: {
    label: { type: 'string' },
    color: { type: 'string' },
  },
  required: ['label', 'color'],
} as const;

export const canvasSchema: RxJsonSchema<Canvas> = {
  title: 'canvas schema',
  version: 0,
  description: 'A board. A user can own several.',
  primaryKey: 'id',
  type: 'object',
  properties: {
    id: { type: 'string', maxLength: 64 },
    ownerId: { type: 'string', maxLength: 64 },
    name: { type: 'string' },
    createdAt: { type: 'number' },
    updatedAt: { type: 'number' },
  },
  required: ['id', 'ownerId', 'name', 'createdAt', 'updatedAt'],
};

export const pageSchema: RxJsonSchema<Page> = {
  title: 'page schema',
  version: 0,
  description: 'A note/card. body is markdown — the source of truth.',
  primaryKey: 'id',
  type: 'object',
  properties: {
    id: { type: 'string', maxLength: 64 },
    canvasId: { type: 'string', maxLength: 64 },
    title: { type: 'string' },
    body: { type: 'string' },
    x: { type: 'number' },
    y: { type: 'number' },
    width: { type: 'number' },
    height: { type: 'number' },
    groupId: { type: ['string', 'null'], maxLength: 64 },
    order: { type: 'number' },
    color: { type: ['string', 'null'] },
    badges: { type: 'array', items: badgeSchema },
    createdAt: { type: 'number' },
    updatedAt: { type: 'number' },
  },
  required: [
    'id',
    'canvasId',
    'title',
    'body',
    'x',
    'y',
    'width',
    'height',
    'order',
    'createdAt',
    'updatedAt',
  ],
  indexes: ['canvasId'],
};

export const groupSchema: RxJsonSchema<Group> = {
  title: 'group schema',
  version: 0,
  description: 'A column or a row, discriminated by type.',
  primaryKey: 'id',
  type: 'object',
  properties: {
    id: { type: 'string', maxLength: 64 },
    canvasId: { type: 'string', maxLength: 64 },
    type: { type: 'string', enum: ['column', 'row'], maxLength: 6 },
    name: { type: 'string' },
    color: { type: ['string', 'null'] },
    badges: { type: 'array', items: badgeSchema },
    order: { type: 'number' },
    x: { type: 'number' },
    y: { type: 'number' },
    createdAt: { type: 'number' },
    updatedAt: { type: 'number' },
  },
  required: [
    'id',
    'canvasId',
    'type',
    'name',
    'order',
    'x',
    'y',
    'createdAt',
    'updatedAt',
  ],
  indexes: ['canvasId'],
};

/* --- Document & collection helper types -------------------------------- */

export type CanvasDocument = RxDocument<Canvas>;
export type PageDocument = RxDocument<Page>;
export type GroupDocument = RxDocument<Group>;

export type CanvasCollection = RxCollection<Canvas>;
export type PageCollection = RxCollection<Page>;
export type GroupCollection = RxCollection<Group>;

export interface ReamCollections {
  canvases: CanvasCollection;
  pages: PageCollection;
  groups: GroupCollection;
}

export type { Badge };

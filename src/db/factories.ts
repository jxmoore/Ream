/* Factory helpers that build complete, schema-valid documents with sensible
   defaults. Centralizing them keeps services and the seed in sync. */

import { createId } from '../lib/id';
import {
  DEFAULT_CARD_WIDTH,
  DEFAULT_CARD_HEIGHT,
} from '../lib/constants';
import type { Canvas, Page, Group, GroupType } from './types';

export function buildCanvas(input: Partial<Canvas> & { name: string }): Canvas {
  const now = Date.now();
  return {
    id: input.id ?? createId(),
    ownerId: input.ownerId ?? 'local',
    name: input.name,
    createdAt: input.createdAt ?? now,
    updatedAt: input.updatedAt ?? now,
  };
}

export function buildPage(
  input: Partial<Page> & { canvasId: string },
): Page {
  const now = Date.now();
  return {
    id: input.id ?? createId(),
    canvasId: input.canvasId,
    title: input.title ?? 'Untitled',
    body: input.body ?? '',
    x: input.x ?? 0,
    y: input.y ?? 0,
    width: input.width ?? DEFAULT_CARD_WIDTH,
    height: input.height ?? DEFAULT_CARD_HEIGHT,
    groupId: input.groupId ?? null,
    order: input.order ?? 0,
    color: input.color ?? null,
    badges: input.badges ?? [],
    createdAt: input.createdAt ?? now,
    updatedAt: input.updatedAt ?? now,
  };
}

export function buildGroup(
  input: Partial<Group> & { canvasId: string; type: GroupType },
): Group {
  const now = Date.now();
  return {
    id: input.id ?? createId(),
    canvasId: input.canvasId,
    type: input.type,
    name: input.name ?? '',
    color: input.color ?? null,
    badges: input.badges ?? [],
    order: input.order ?? 0,
    x: input.x ?? 0,
    y: input.y ?? 0,
    createdAt: input.createdAt ?? now,
    updatedAt: input.updatedAt ?? now,
  };
}

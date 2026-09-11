import { nanoid } from 'nanoid';

/** String primary key for every record (§3: never auto-increment integers). */
export function createId(): string {
  return nanoid();
}

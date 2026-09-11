/* Configured TipTap extension set. Markdown is the source of truth (§3):
   tiptap-markdown parses markdown passed as content and exposes
   editor.storage.markdown.getMarkdown() for serialization back out. */

import StarterKit from '@tiptap/starter-kit';
import Underline from '@tiptap/extension-underline';
import Link from '@tiptap/extension-link';
import Placeholder from '@tiptap/extension-placeholder';
import { Markdown } from 'tiptap-markdown';

export const editorExtensions = [
  StarterKit.configure({
    heading: { levels: [1, 2, 3] },
  }),
  Underline,
  Link.configure({
    openOnClick: false,
    autolink: true,
    HTMLAttributes: { rel: 'noopener noreferrer', target: '_blank' },
  }),
  Placeholder.configure({
    placeholder: 'Start writing… markdown shortcuts work here.',
  }),
  Markdown.configure({
    html: false,
    tightLists: true,
    transformPastedText: true,
    transformCopiedText: true,
    linkify: true,
  }),
];

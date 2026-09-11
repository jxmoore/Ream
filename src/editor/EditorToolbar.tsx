import { useCallback } from 'react';
import type { Editor } from '@tiptap/react';
import { IconButton } from '../components/Button';
import type { IconName } from '../components/Icon';
import './EditorToolbar.css';

interface EditorToolbarProps {
  editor: Editor;
}

interface ToolDef {
  icon: IconName;
  label: string;
  isActive: () => boolean;
  run: () => void;
}

export function EditorToolbar({ editor }: EditorToolbarProps) {
  const promptLink = useCallback(() => {
    const previous = editor.getAttributes('link').href as string | undefined;
    const url = window.prompt('Link URL', previous ?? 'https://');
    if (url === null) return;
    if (url === '') {
      editor.chain().focus().extendMarkRange('link').unsetLink().run();
      return;
    }
    editor.chain().focus().extendMarkRange('link').setLink({ href: url }).run();
  }, [editor]);

  const groups: ToolDef[][] = [
    [
      {
        icon: 'bold',
        label: 'Bold',
        isActive: () => editor.isActive('bold'),
        run: () => editor.chain().focus().toggleBold().run(),
      },
      {
        icon: 'italic',
        label: 'Italic',
        isActive: () => editor.isActive('italic'),
        run: () => editor.chain().focus().toggleItalic().run(),
      },
      {
        icon: 'underline',
        label: 'Underline',
        isActive: () => editor.isActive('underline'),
        run: () => editor.chain().focus().toggleUnderline().run(),
      },
      {
        icon: 'strike',
        label: 'Strikethrough',
        isActive: () => editor.isActive('strike'),
        run: () => editor.chain().focus().toggleStrike().run(),
      },
    ],
    [
      {
        icon: 'h1',
        label: 'Heading 1',
        isActive: () => editor.isActive('heading', { level: 1 }),
        run: () => editor.chain().focus().toggleHeading({ level: 1 }).run(),
      },
      {
        icon: 'h2',
        label: 'Heading 2',
        isActive: () => editor.isActive('heading', { level: 2 }),
        run: () => editor.chain().focus().toggleHeading({ level: 2 }).run(),
      },
    ],
    [
      {
        icon: 'list',
        label: 'Bullet list',
        isActive: () => editor.isActive('bulletList'),
        run: () => editor.chain().focus().toggleBulletList().run(),
      },
      {
        icon: 'list-ordered',
        label: 'Numbered list',
        isActive: () => editor.isActive('orderedList'),
        run: () => editor.chain().focus().toggleOrderedList().run(),
      },
      {
        icon: 'quote',
        label: 'Quote',
        isActive: () => editor.isActive('blockquote'),
        run: () => editor.chain().focus().toggleBlockquote().run(),
      },
      {
        icon: 'code',
        label: 'Code block',
        isActive: () => editor.isActive('codeBlock'),
        run: () => editor.chain().focus().toggleCodeBlock().run(),
      },
    ],
    [
      {
        icon: 'link',
        label: 'Link',
        isActive: () => editor.isActive('link'),
        run: promptLink,
      },
    ],
  ];

  return (
    <div className="editor-toolbar" role="toolbar" aria-label="Formatting">
      {groups.map((group, gi) => (
        <div className="editor-toolbar__group" key={gi}>
          {group.map((tool) => (
            <IconButton
              key={tool.label}
              icon={tool.icon}
              label={tool.label}
              size="sm"
              active={tool.isActive()}
              onClick={tool.run}
            />
          ))}
        </div>
      ))}
      <div className="ream-spacer" />
      <div className="editor-toolbar__group">
        <IconButton
          icon="undo"
          label="Undo"
          size="sm"
          disabled={!editor.can().undo()}
          onClick={() => editor.chain().focus().undo().run()}
        />
        <IconButton
          icon="redo"
          label="Redo"
          size="sm"
          disabled={!editor.can().redo()}
          onClick={() => editor.chain().focus().redo().run()}
        />
      </div>
    </div>
  );
}

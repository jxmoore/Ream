/* A tiny inline-SVG icon set (stroke-based, 24x24). Keeps the bundle lean and
   icons themeable via `currentColor`. */

import type { SVGProps } from 'react';

export type IconName =
  | 'plus'
  | 'column'
  | 'row'
  | 'trash'
  | 'edit'
  | 'download'
  | 'image'
  | 'close'
  | 'zoom-in'
  | 'zoom-out'
  | 'fit'
  | 'check'
  | 'palette'
  | 'tag'
  | 'file'
  | 'chevron-down'
  | 'more'
  | 'bold'
  | 'italic'
  | 'underline'
  | 'strike'
  | 'h1'
  | 'h2'
  | 'list'
  | 'list-ordered'
  | 'quote'
  | 'code'
  | 'link'
  | 'undo'
  | 'redo'
  | 'canvas';

const PATHS: Record<IconName, string> = {
  plus: 'M12 5v14M5 12h14',
  column: 'M4 4h6v16H4zM14 4h6v16h-6z',
  row: 'M4 4h16v6H4zM4 14h16v6H4z',
  trash: 'M4 7h16M9 7V5a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2m2 0v12a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1V7',
  edit: 'M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4',
  download: 'M12 4v11m0 0l-4-4m4 4l4-4M5 19h14',
  image: 'M4 5h16v14H4zM4 15l4-4 4 4 3-3 5 5M9 9a1.5 1.5 0 1 1-3 0 1.5 1.5 0 0 1 3 0z',
  close: 'M6 6l12 12M18 6L6 18',
  'zoom-in': 'M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14zM11 8v6M8 11h6M16 16l4 4',
  'zoom-out': 'M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14zM8 11h6M16 16l4 4',
  fit: 'M4 9V5a1 1 0 0 1 1-1h4M15 4h4a1 1 0 0 1 1 1v4M20 15v4a1 1 0 0 1-1 1h-4M9 20H5a1 1 0 0 1-1-1v-4',
  check: 'M5 12l5 5L20 6',
  palette: 'M12 3a9 9 0 1 0 0 18c1.1 0 2-.9 2-2 0-.5-.2-1-.5-1.3-.3-.4-.5-.8-.5-1.2 0-1 .9-1.5 2-1.5h2a3 3 0 0 0 3-3c0-4.4-3.6-8-8-8z',
  tag: 'M4 4h7l9 9-7 7-9-9V4zM7.5 7.5h.01',
  file: 'M6 3h8l4 4v14H6zM14 3v4h4',
  'chevron-down': 'M6 9l6 6 6-6',
  more: 'M6 12h.01M12 12h.01M18 12h.01',
  bold: 'M7 5h6a3.5 3.5 0 0 1 0 7H7zM7 12h7a3.5 3.5 0 0 1 0 7H7z',
  italic: 'M10 4h6M8 20h6M14 4l-4 16',
  underline: 'M7 4v7a5 5 0 0 0 10 0V4M5 21h14',
  strike: 'M5 12h14M8 7a4 3 0 0 1 8 0M8 16a4 3 0 0 0 8 0',
  h1: 'M4 6v12M4 12h8M12 6v12M17 9l2-1.5V18',
  h2: 'M4 6v12M4 12h8M12 6v12M16 10a2 2 0 1 1 3.5 1.3L16 18h5',
  list: 'M9 6h11M9 12h11M9 18h11M4.5 6h.01M4.5 12h.01M4.5 18h.01',
  'list-ordered': 'M10 6h10M10 12h10M10 18h10M4 6h1v4M4 10h2M4 14h2l-2 2.5h2',
  quote: 'M6 7h5v5a4 4 0 0 1-4 4H6zM14 7h5v5a4 4 0 0 1-4 4h-1',
  code: 'M9 8l-5 4 5 4M15 8l5 4-5 4',
  link: 'M10 14a3.5 3.5 0 0 0 5 0l3-3a3.5 3.5 0 0 0-5-5l-1 1M14 10a3.5 3.5 0 0 0-5 0l-3 3a3.5 3.5 0 0 0 5 5l1-1',
  undo: 'M9 7L4 12l5 5M4 12h11a5 5 0 0 1 0 10h-1',
  redo: 'M15 7l5 5-5 5M20 12H9a5 5 0 0 0 0 10h1',
  canvas: 'M4 4h16v16H4zM4 10h16M10 4v16',
};

interface IconProps extends SVGProps<SVGSVGElement> {
  name: IconName;
  size?: number;
}

export function Icon({ name, size = 18, ...rest }: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      {...rest}
    >
      <path d={PATHS[name]} />
    </svg>
  );
}

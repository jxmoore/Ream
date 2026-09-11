import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { Icon, type IconName } from './Icon';
import './Button.css';

type Variant = 'primary' | 'default' | 'ghost' | 'danger';
type Size = 'sm' | 'md';

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  size?: Size;
  icon?: IconName;
  children?: ReactNode;
}

export function Button({
  variant = 'default',
  size = 'md',
  icon,
  children,
  className = '',
  ...rest
}: ButtonProps) {
  return (
    <button
      className={`btn btn--${variant} btn--${size} ${
        children ? '' : 'btn--icon-only'
      } ${className}`}
      {...rest}
    >
      {icon && <Icon name={icon} size={size === 'sm' ? 16 : 18} />}
      {children && <span>{children}</span>}
    </button>
  );
}

interface IconButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  icon: IconName;
  label: string;
  variant?: Variant;
  size?: Size;
  active?: boolean;
}

/** An icon-only button with an accessible label + tooltip. */
export function IconButton({
  icon,
  label,
  variant = 'ghost',
  size = 'md',
  active = false,
  className = '',
  ...rest
}: IconButtonProps) {
  return (
    <button
      className={`btn btn--${variant} btn--${size} btn--icon-only ${
        active ? 'btn--active' : ''
      } ${className}`}
      aria-label={label}
      aria-pressed={active}
      title={label}
      {...rest}
    >
      <Icon name={icon} size={size === 'sm' ? 16 : 18} />
    </button>
  );
}

/* Minimal toast notifications. `useToast().notify(message, level)` from
   anywhere under <ToastProvider>. */

import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { createPortal } from 'react-dom';
import { createId } from '../lib/id';
import './Toast.css';

export type ToastLevel = 'info' | 'success' | 'error';

interface Toast {
  id: string;
  message: string;
  level: ToastLevel;
}

interface ToastContextValue {
  notify: (message: string, level?: ToastLevel) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const timers = useRef<Map<string, ReturnType<typeof setTimeout>>>(new Map());

  const dismiss = useCallback((id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
    const timer = timers.current.get(id);
    if (timer) clearTimeout(timer);
    timers.current.delete(id);
  }, []);

  const notify = useCallback(
    (message: string, level: ToastLevel = 'info') => {
      const id = createId();
      setToasts((prev) => [...prev.slice(-3), { id, message, level }]);
      timers.current.set(
        id,
        setTimeout(() => dismiss(id), level === 'error' ? 6000 : 3500),
      );
    },
    [dismiss],
  );

  const value = useMemo(() => ({ notify }), [notify]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      {createPortal(
        <div className="toast-stack" role="status" aria-live="polite">
          {toasts.map((t) => (
            <button
              key={t.id}
              className={`toast toast--${t.level}`}
              onClick={() => dismiss(t.id)}
            >
              {t.message}
            </button>
          ))}
        </div>,
        document.body,
      )}
    </ToastContext.Provider>
  );
}

export function useToast(): ToastContextValue {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error('useToast must be used within ToastProvider');
  return ctx;
}

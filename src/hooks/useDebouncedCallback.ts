import { useCallback, useEffect, useRef } from 'react';

/** Returns a debounced version of `fn` plus a `flush()` that runs it now. */
export function useDebouncedCallback<A extends unknown[]>(
  fn: (...args: A) => void,
  delay: number,
): { call: (...args: A) => void; flush: () => void; cancel: () => void } {
  const fnRef = useRef(fn);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lastArgs = useRef<A | null>(null);

  useEffect(() => {
    fnRef.current = fn;
  }, [fn]);

  const clear = () => {
    if (timer.current) {
      clearTimeout(timer.current);
      timer.current = null;
    }
  };

  const call = useCallback(
    (...args: A) => {
      lastArgs.current = args;
      clear();
      timer.current = setTimeout(() => {
        timer.current = null;
        if (lastArgs.current) fnRef.current(...lastArgs.current);
      }, delay);
    },
    [delay],
  );

  const flush = useCallback(() => {
    if (timer.current && lastArgs.current) {
      clear();
      fnRef.current(...lastArgs.current);
    }
  }, []);

  const cancel = useCallback(() => {
    clear();
    lastArgs.current = null;
  }, []);

  useEffect(() => clear, []);

  return { call, flush, cancel };
}

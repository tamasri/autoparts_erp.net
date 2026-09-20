import { useCallback, useEffect, useRef, useState } from 'react';
import { extractApiError } from '../lib/toast';

/**
 * Loads one thing when the screen opens and again whenever <paramref name="deps"/> change (filters). Skips the load while
 * <paramref name="enabled"/> is false, and drops a slow earlier answer that arrives after a newer request.
 */
export function useLoad<T>(loader: () => Promise<T>, deps: ReadonlyArray<unknown>, errorMessage: string, enabled = true): {
  data: T | null; loading: boolean; error: string; reload: () => void;
} {
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [tick, setTick] = useState(0);
  const latest = useRef(0);
  const loaderRef = useRef(loader);
  loaderRef.current = loader;

  useEffect(() => {
    if (!enabled) { setData(null); return; }
    const id = ++latest.current;
    setLoading(true); setError('');
    loaderRef.current()
      .then((value) => { if (id === latest.current) setData(value); })
      .catch((e: unknown) => { if (id === latest.current) { setData(null); setError(extractApiError(e, errorMessage)); } })
      .finally(() => { if (id === latest.current) setLoading(false); });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tick, enabled, ...deps]);

  const reload = useCallback(() => setTick((t) => t + 1), []);
  return { data, loading, error, reload };
}

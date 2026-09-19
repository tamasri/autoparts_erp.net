import { useCallback, useEffect, useRef, useState } from 'react';
import { unwrapPaged } from '../api/apiData';

type Fetcher = (params: { page: number; pageSize: number; search: string }) => Promise<{ data: unknown }>;

type Options = {
  fetcher: Fetcher;
  pageSize?: number;
  errorMessage: string;
  /** Any change to these values resets to page 1 and reloads (filters, tabs). */
  deps?: ReadonlyArray<unknown>;
  searchDebounceMs?: number;
};

/**
 * Server-side paging + debounced search for list screens. Only one page of rows is ever held in
 * memory, so screens stay fast however large the table grows. A monotonically increasing request
 * id discards out-of-order responses (a slow earlier page can never overwrite a newer one).
 */
export function usePagedList<T>({ fetcher, pageSize: initialPageSize = 20, errorMessage, deps = [], searchDebounceMs = 350 }: Options) {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(initialPageSize);
  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const [items, setItems] = useState<T[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [reloadTick, setReloadTick] = useState(0);
  const requestId = useRef(0);
  const fetcherRef = useRef(fetcher);
  fetcherRef.current = fetcher;

  useEffect(() => {
    const handle = window.setTimeout(() => {
      setSearch(searchInput.trim());
      setPage(1);
    }, searchDebounceMs);
    return () => window.clearTimeout(handle);
  }, [searchInput, searchDebounceMs]);

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { setPage(1); }, deps);

  useEffect(() => {
    const id = ++requestId.current;
    setLoading(true);
    setError('');
    fetcherRef.current({ page, pageSize, search })
      .then((res) => {
        if (id !== requestId.current) return;
        const paged = unwrapPaged<T>(res.data);
        setItems(paged.items);
        setTotalCount(paged.totalCount);
      })
      .catch((e: unknown) => {
        if (id !== requestId.current) return;
        const r = e as { response?: { data?: { detail?: string; message?: string } } };
        setError(r.response?.data?.detail ?? r.response?.data?.message ?? errorMessage);
        setItems([]);
        setTotalCount(0);
      })
      .finally(() => { if (id === requestId.current) setLoading(false); });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, pageSize, search, reloadTick, ...deps]);

  const reload = useCallback(() => setReloadTick((t) => t + 1), []);
  const changePageSize = useCallback((size: number) => { setPageSize(size); setPage(1); }, []);

  return { items, totalCount, page, pageSize, setPage, changePageSize, searchInput, setSearchInput, search, loading, error, reload };
}

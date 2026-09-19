import { useEffect, useState } from 'react';
import { dashboardApi, type DashboardSummary } from '../api/endpoints/dashboard';
import { unwrapNode } from '../api/apiData';
import { extractApiError } from '../lib/toast';

/** Loads the server-aggregated dashboard figures (computed over the full data set, not a sample page). */
export function useDashboardSummary(errorMessage: string): { data: DashboardSummary | null; loading: boolean; error: string } {
  const [data, setData] = useState<DashboardSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    let live = true;
    dashboardApi.getSummary()
      .then((res) => { if (live) setData(unwrapNode<DashboardSummary>(res.data)); })
      .catch((e: unknown) => { if (live) setError(extractApiError(e, errorMessage)); })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
  }, [errorMessage]);

  return { data, loading, error };
}

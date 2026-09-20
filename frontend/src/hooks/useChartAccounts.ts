import { useCallback, useEffect, useMemo, useState } from 'react';
import { accountingApi, type Account } from '../api/endpoints/accounting';
import { unwrapList } from '../api/apiData';
import { extractApiError } from '../lib/toast';

/** The chart of accounts (without balances) for pickers: all accounts, and just the ones that take postings. */
export function useChartAccounts(): { accounts: Account[]; ledgers: Account[]; loading: boolean; error: string; reload: () => void } {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [tick, setTick] = useState(0);

  useEffect(() => {
    let live = true;
    setLoading(true);
    accountingApi.accounts(false)
      .then((r) => { if (live) { setAccounts(unwrapList<Account>(r.data)); setError(''); } })
      .catch((e: unknown) => { if (live) setError(extractApiError(e, 'تعذر قراءة شجرة الحسابات')); })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
  }, [tick]);

  const ledgers = useMemo(() => accounts.filter((a) => !a.isGroup), [accounts]);
  const reload = useCallback(() => setTick((t) => t + 1), []);
  return { accounts, ledgers, loading, error, reload };
}

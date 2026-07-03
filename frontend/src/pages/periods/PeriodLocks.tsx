import { useEffect, useState } from 'react';
import { periodsApi } from '../../api/endpoints/periods';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

type PeriodLock = {
  id: string;
  periodKey?: string;
  moduleCode?: string;
  isLocked?: boolean;
};

const monthNames = ['يناير', 'فبراير', 'مارس', 'أبريل', 'مايو', 'يونيو', 'يوليو', 'أغسطس', 'سبتمبر', 'أكتوبر', 'نوفمبر', 'ديسمبر'];

export default function PeriodLocks(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [locks, setLocks] = useState<PeriodLock[]>([]);

  useEffect(() => {
    let mounted = true;
    async function load(): Promise<void> {
      setLoading(true);
      setError('');
      try {
        const year = new Date().getFullYear();
        const res = await periodsApi.getLocks(year);
        if (mounted) setLocks(unwrapList<PeriodLock>(res.data));
      } catch (e: unknown) {
        if (!mounted) return;
        const msg = (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.detail
          ?? (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.message
          ?? 'تعذر تحميل إقفال الفترات';
        setError(msg);
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void load();
    return () => {
      mounted = false;
    };
  }, []);

  if (loading) return <LoadingSpinner />;

  const currentYear = new Date().getFullYear();

  return (
    <div style={{ direction: 'rtl' }}>
      {error ? <ErrorBanner message={error} /> : null}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))', gap: '12px' }}>
        {monthNames.map((name, index) => {
          const periodKey = `${currentYear}-${String(index + 1).padStart(2, '0')}`;
          const locked = locks.some((l) => l.periodKey === periodKey && l.isLocked);
          return (
            <div key={periodKey} style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', padding: '12px' }}>
              <div style={{ fontWeight: 700 }}>{name}</div>
              <div style={{ color: locked ? '#c62828' : '#2e7d32', margin: '6px 0' }}>{locked ? '🔒 مقفل' : '🔓 مفتوح'}</div>
              {!locked && (
                <button
                  type="button"
                  onClick={async () => {
                    await periodsApi.lockPeriod({ periodKey, moduleCode: 'SALES', reason: 'Lock from UI' });
                    const res = await periodsApi.getLocks(currentYear);
                    setLocks(unwrapList<PeriodLock>(res.data));
                  }}
                  style={{ border: 'none', borderRadius: '8px', background: '#00796b', color: '#fff', padding: '8px 10px' }}
                >
                  طلب إقفال
                </button>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}

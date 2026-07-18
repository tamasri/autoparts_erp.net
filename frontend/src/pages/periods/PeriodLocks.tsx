import { useEffect, useState } from 'react';
import { periodsApi } from '../../api/endpoints/periods';
import { unwrapList } from '../../api/apiData';
import { toast, extractApiError } from '../../lib/toast';
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
  const [busy, setBusy] = useState('');
  const currentYear = new Date().getFullYear();

  async function load(): Promise<void> {
    setLoading(true);
    setError('');
    try {
      const res = await periodsApi.getLocks(currentYear);
      setLocks(unwrapList<PeriodLock>(res.data));
    } catch (e: unknown) {
      setError(extractApiError(e, 'تعذر تحميل إقفال الفترات'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void load(); }, []);

  async function lockPeriod(periodKey: string): Promise<void> {
    setBusy(periodKey);
    try {
      await periodsApi.lockPeriod({ periodKey, moduleCode: 'SALES', reason: 'Lock from UI' });
      toast.success(`تم إقفال الفترة ${periodKey}`);
      await load();
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر إقفال الفترة'));
    } finally {
      setBusy('');
    }
  }

  async function unlockPeriod(periodKey: string): Promise<void> {
    setBusy(periodKey);
    try {
      await periodsApi.unlockPeriod({ periodKey, moduleCode: 'SALES', reason: 'Unlock from UI' });
      toast.success(`تم فتح الفترة ${periodKey}`);
      await load();
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر فتح الفترة'));
    } finally {
      setBusy('');
    }
  }

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      {error ? <ErrorBanner message={error} /> : null}
      <h2 style={{ marginTop: 0 }}>إقفال الفترات — {currentYear}</h2>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))', gap: '12px' }}>
        {monthNames.map((name, index) => {
          const periodKey = `${currentYear}-${String(index + 1).padStart(2, '0')}`;
          const locked = locks.some((l) => l.periodKey === periodKey && l.isLocked);
          const isBusy = busy === periodKey;
          return (
            <div key={periodKey} style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', padding: '14px', borderRight: `4px solid ${locked ? '#c62828' : '#2e7d32'}` }}>
              <div style={{ fontWeight: 700, marginBottom: '6px' }}>{name}</div>
              <div style={{ color: locked ? '#c62828' : '#2e7d32', marginBottom: '10px', fontSize: '13px' }}>
                {locked ? '🔒 مقفل' : '🔓 مفتوح'}
              </div>
              {locked ? (
                <button
                  type="button"
                  disabled={isBusy}
                  onClick={() => void unlockPeriod(periodKey)}
                  style={btn('#607d8b')}
                >
                  {isBusy ? '...' : 'فتح'}
                </button>
              ) : (
                <button
                  type="button"
                  disabled={isBusy}
                  onClick={() => void lockPeriod(periodKey)}
                  style={btn('#00796b')}
                >
                  {isBusy ? '...' : 'إقفال'}
                </button>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}

function btn(bg: string): React.CSSProperties {
  return { border: 'none', borderRadius: '8px', background: bg, color: '#fff', padding: '7px 12px', cursor: 'pointer', fontSize: '13px', opacity: 1 };
}

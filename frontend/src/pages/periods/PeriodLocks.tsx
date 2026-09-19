import { useEffect, useState } from 'react';
import { periodsApi } from '../../api/endpoints/periods';
import { unwrapList } from '../../api/apiData';
import { toast, extractApiError } from '../../lib/toast';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

type PeriodLock = { id: string; periodKey?: string; moduleCode?: string; isLocked?: boolean };

const MONTHS = ['يناير', 'فبراير', 'مارس', 'أبريل', 'مايو', 'يونيو', 'يوليو', 'أغسطس', 'سبتمبر', 'أكتوبر', 'نوفمبر', 'ديسمبر'];

export default function PeriodLocks(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [locks, setLocks] = useState<PeriodLock[]>([]);
  const [busy, setBusy] = useState('');
  const currentYear = new Date().getFullYear();
  const currentMonth = new Date().getMonth(); // 0-indexed

  async function load(): Promise<void> {
    setLoading(true); setError('');
    try { const res = await periodsApi.getLocks(currentYear); setLocks(unwrapList<PeriodLock>(res.data)); }
    catch (e: unknown) { setError(extractApiError(e, 'تعذر تحميل إقفال الفترات')); }
    finally { setLoading(false); }
  }

  useEffect(() => { void load(); }, []);

  async function lockPeriod(periodKey: string): Promise<void> {
    setBusy(periodKey);
    try { await periodsApi.lockPeriod({ periodKey, moduleCode: 'SALES', reason: 'Lock from UI' }); toast.success(`تم إقفال الفترة ${periodKey}`); await load(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر إقفال الفترة')); }
    finally { setBusy(''); }
  }

  async function unlockPeriod(periodKey: string): Promise<void> {
    setBusy(periodKey);
    try { await periodsApi.unlockPeriod({ periodKey, moduleCode: 'SALES', reason: 'Unlock from UI' }); toast.success(`تم فتح الفترة ${periodKey}`); await load(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر فتح الفترة')); }
    finally { setBusy(''); }
  }

  if (loading) return <LoadingSpinner />;

  const lockedCount = MONTHS.filter((_, i) => {
    const pk = `${currentYear}-${String(i + 1).padStart(2, '0')}`;
    return locks.some((l) => l.periodKey === pk && l.isLocked);
  }).length;

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">إقفال الفترات — {currentYear}</h1>
          <div className="vex-page-header__breadcrumb">إدارة حالات فتح وإقفال الفترات المحاسبية</div>
        </div>
        <div style={{ display: 'flex', gap: 10 }}>
          <div style={{ background: '#fef2f2', color: 'var(--clr-danger)', border: '1px solid #fecaca', borderRadius: 'var(--radius-pill)', padding: '6px 14px', fontSize: 13, fontWeight: 700 }}>
            🔒 مقفل: {lockedCount}
          </div>
          <div style={{ background: '#f0fdf4', color: '#15803d', border: '1px solid #bbf7d0', borderRadius: 'var(--radius-pill)', padding: '6px 14px', fontSize: 13, fontWeight: 700 }}>
            🔓 مفتوح: {12 - lockedCount}
          </div>
        </div>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 14 }}>
        {MONTHS.map((name, index) => {
          const periodKey = `${currentYear}-${String(index + 1).padStart(2, '0')}`;
          const locked = locks.some((l) => l.periodKey === periodKey && l.isLocked);
          const isBusy = busy === periodKey;
          const isCurrent = index === currentMonth;

          return (
            <div
              key={periodKey}
              className="vex-card"
              style={{
                borderRight: `4px solid ${locked ? 'var(--clr-danger)' : '#22c55e'}`,
                background: locked
                  ? 'linear-gradient(135deg, #fef2f2, #fff)'
                  : isCurrent
                    ? 'linear-gradient(135deg, #f0fdf4, #fff)'
                    : '#fff',
                position: 'relative',
                overflow: 'hidden',
              }}
            >
              {isCurrent && (
                <div style={{
                  position: 'absolute', top: 6, left: 6,
                  background: 'var(--clr-primary)', color: '#fff',
                  fontSize: 9, fontWeight: 700, padding: '2px 6px',
                  borderRadius: 'var(--radius-sm)', letterSpacing: '0.5px',
                }}>الشهر الحالي</div>
              )}
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'start', marginBottom: 10 }}>
                <div>
                  <div style={{ fontWeight: 700, fontSize: 15, color: 'var(--txt-primary)', marginBottom: 4 }}>{name}</div>
                  <div style={{ fontSize: 11, color: 'var(--txt-muted)' }}>{periodKey}</div>
                </div>
                <span style={{ fontSize: 20 }}>{locked ? '🔒' : '🔓'}</span>
              </div>

              <div style={{ marginBottom: 12 }}>
                <span className={`badge ${locked ? 'badge--danger' : 'badge--success'}`} style={{ fontSize: 11 }}>
                  {locked ? 'مقفل' : 'مفتوح'}
                </span>
              </div>

              {locked ? (
                <button
                  type="button"
                  disabled={isBusy}
                  onClick={() => void unlockPeriod(periodKey)}
                  className="btn-ghost"
                  style={{ width: '100%', padding: '7px 0', fontSize: 13 }}
                >
                  {isBusy ? '...' : '🔓 فتح'}
                </button>
              ) : (
                <button
                  type="button"
                  disabled={isBusy}
                  onClick={() => void lockPeriod(periodKey)}
                  className="btn-danger"
                  style={{ width: '100%', padding: '7px 0', fontSize: 13 }}
                >
                  {isBusy ? '...' : '🔒 إقفال'}
                </button>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}

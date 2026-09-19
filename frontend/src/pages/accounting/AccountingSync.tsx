import { useCallback, useEffect, useMemo, useState } from 'react';
import { erpnextApi } from '../../api/endpoints/erpnext';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';
import StatusBadge from '../../components/common/StatusBadge';

type SyncRow = {
  localEntityType: string;
  erpnextDoctype: string;
  status: string;
  erpnextName?: string | null;
  lastError?: string | null;
  attemptCount: number;
  updatedAt: string;
};

const doctypeLabels: Record<string, string> = {
  Item: 'أصناف',
  Customer: 'عملاء',
  Supplier: 'موردون',
  'Sales Invoice': 'فواتير مبيعات',
  'Payment Entry': 'مدفوعات',
};

export default function AccountingSync(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [error, setError] = useState('');
  const [info, setInfo] = useState('');
  const [rows, setRows] = useState<SyncRow[]>([]);

  const load = useCallback(async (): Promise<void> => {
    setError('');
    try {
      const res = await erpnextApi.getSyncLog();
      setRows(unwrapList<SyncRow>(res.data));
    } catch (e: unknown) {
      const r = e as { response?: { status?: number; data?: { detail?: string; message?: string } } };
      setError(
        r.response?.status === 403
          ? 'هذه الشاشة متاحة لمدير النظام فقط'
          : r.response?.data?.detail ?? r.response?.data?.message ?? 'تعذر تحميل حالة المزامنة',
      );
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function runSync(): Promise<void> {
    setSyncing(true); setError(''); setInfo('');
    try {
      await erpnextApi.triggerSync();
      setInfo('بدأت المزامنة في الخلفية. حدّث الشاشة بعد لحظات لرؤية النتيجة.');
    } catch (e: unknown) {
      const r = e as { response?: { status?: number; data?: { error?: string; detail?: string } } };
      setError(
        r.response?.status === 409
          ? 'تكامل ERPNext غير مفعّل في إعدادات الخادم'
          : r.response?.data?.error ?? r.response?.data?.detail ?? 'تعذر بدء المزامنة',
      );
    } finally {
      setSyncing(false);
    }
  }

  const summary = useMemo(() => {
    const groups = new Map<string, { synced: number; failed: number; skipped: number }>();
    for (const row of rows) {
      const g = groups.get(row.erpnextDoctype) ?? { synced: 0, failed: 0, skipped: 0 };
      if (row.status === 'SYNCED') g.synced += 1;
      else if (row.status === 'FAILED') g.failed += 1;
      else g.skipped += 1;
      groups.set(row.erpnextDoctype, g);
    }
    return [...groups.entries()];
  }, [rows]);

  const failures = rows.filter((r) => r.status === 'FAILED');

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">مزامنة المحاسبة</h1>
          <div className="vex-page-header__breadcrumb">حالة ترحيل الأصناف والعملاء والفواتير إلى دفتر الأستاذ</div>
        </div>
        <div style={{ display: 'flex', gap: 10 }}>
          <button type="button" className="btn-ghost" onClick={() => { setLoading(true); void load(); }}>↺ تحديث</button>
          <button type="button" className="btn-primary" disabled={syncing} onClick={() => void runSync()}>
            {syncing ? 'جارٍ البدء...' : '⇄ مزامنة الآن'}
          </button>
        </div>
      </div>

      {error ? <ErrorBanner message={error} /> : null}
      {info ? <div className="vex-card" style={{ marginBottom: 16, color: 'var(--clr-primary)' }}>{info}</div> : null}

      {loading ? <LoadingSpinner /> : (
        <>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 16, marginBottom: 20 }}>
            {summary.length === 0 ? (
              <div className="vex-card" style={{ color: 'var(--txt-muted)' }}>لا توجد عمليات مزامنة بعد</div>
            ) : summary.map(([doctype, g]) => (
              <div key={doctype} className="vex-card">
                <div style={{ fontWeight: 700, marginBottom: 8 }}>{doctypeLabels[doctype] ?? doctype}</div>
                <div style={{ fontSize: 13, color: 'var(--txt-secondary)' }}>
                  ✓ {g.synced} تمت &nbsp; ✗ {g.failed} فشلت &nbsp; ⏸ {g.skipped} متخطاة
                </div>
              </div>
            ))}
          </div>

          {failures.length > 0 ? (
            <div className="vex-card" style={{ marginBottom: 20 }}>
              <h2 className="vex-section-title">أخطاء تحتاج انتباهاً ({failures.length})</h2>
              {failures.slice(0, 20).map((f, i) => (
                <div key={`${f.erpnextDoctype}-${i}`} style={{ fontSize: 12, padding: '6px 0', borderTop: i ? '1px solid var(--clr-border)' : 'none' }}>
                  <strong>{doctypeLabels[f.erpnextDoctype] ?? f.erpnextDoctype}</strong>: {f.lastError ?? '-'}
                </div>
              ))}
            </div>
          ) : null}

          <div className="vex-card vex-card--no-pad">
            <div style={{ overflowX: 'auto' }}>
              <table className="vex-table">
                <thead>
                  <tr>
                    <th>النوع</th>
                    <th>المرجع في المحاسبة</th>
                    <th>الحالة</th>
                    <th>المحاولات</th>
                    <th>آخر تحديث</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.length === 0 ? (
                    <tr><td colSpan={5} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>لا توجد سجلات</td></tr>
                  ) : rows.map((row, i) => (
                    <tr key={`${row.erpnextDoctype}-${row.erpnextName ?? i}-${i}`}>
                      <td>{doctypeLabels[row.erpnextDoctype] ?? row.erpnextDoctype}</td>
                      <td style={{ fontFamily: 'monospace', fontSize: 12 }}>{row.erpnextName ?? '-'}</td>
                      <td><StatusBadge status={row.status} type="approval" /></td>
                      <td>{row.attemptCount}</td>
                      <td style={{ color: 'var(--txt-secondary)', fontSize: 12, whiteSpace: 'nowrap' }}>
                        {new Date(row.updatedAt).toLocaleString('ar')}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

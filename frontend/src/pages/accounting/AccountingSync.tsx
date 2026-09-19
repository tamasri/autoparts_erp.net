import { useCallback, useEffect, useMemo, useState } from 'react';
import { erpnextApi } from '../../api/endpoints/erpnext';
import { unwrapNode } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import Pagination from '../../components/common/Pagination';
import { usePagedList } from '../../hooks/usePagedList';
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

type Summary = { doctype: string; status: string; count: number };
type RecentError = { doctype: string; lastError?: string | null };
type SummaryPayload = { summary: Summary[]; recentErrors: RecentError[] };

const STATUS_FILTERS = [
  { key: '', label: 'الكل' },
  { key: 'SYNCED', label: 'تمت' },
  { key: 'FAILED', label: 'فشلت' },
  { key: 'SKIPPED', label: 'متخطاة' },
];

export default function AccountingSync(): JSX.Element {
  const [syncing, setSyncing] = useState(false);
  const [actionError, setActionError] = useState('');
  const [info, setInfo] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [summaryData, setSummaryData] = useState<SummaryPayload>({ summary: [], recentErrors: [] });

  const list = usePagedList<SyncRow>({
    errorMessage: 'تعذر تحميل حالة المزامنة',
    deps: [statusFilter],
    fetcher: ({ page, pageSize }) => erpnextApi.getSyncLog(page, pageSize, statusFilter),
  });
  const rows = list.items;
  const loading = list.loading;
  const error = actionError || list.error;

  const loadSummary = useCallback(async (): Promise<void> => {
    try {
      const res = await erpnextApi.getSummary();
      setSummaryData(unwrapNode<SummaryPayload>(res.data) ?? { summary: [], recentErrors: [] });
    } catch { /* the list request already surfaces access/connectivity errors */ }
  }, []);

  useEffect(() => { void loadSummary(); }, [loadSummary]);

  function refresh(): void { list.reload(); void loadSummary(); }

  async function runSync(): Promise<void> {
    setSyncing(true); setActionError(''); setInfo('');
    try {
      await erpnextApi.triggerSync();
      setInfo('بدأت المزامنة في الخلفية. حدّث الشاشة بعد لحظات لرؤية النتيجة.');
    } catch (e: unknown) {
      const r = e as { response?: { status?: number; data?: { error?: string; detail?: string } } };
      setActionError(
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
    for (const row of summaryData.summary) {
      const g = groups.get(row.doctype) ?? { synced: 0, failed: 0, skipped: 0 };
      if (row.status === 'SYNCED') g.synced += row.count;
      else if (row.status === 'FAILED') g.failed += row.count;
      else g.skipped += row.count;
      groups.set(row.doctype, g);
    }
    return [...groups.entries()];
  }, [summaryData]);

  const failures = summaryData.recentErrors;

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">مزامنة المحاسبة</h1>
          <div className="vex-page-header__breadcrumb">حالة ترحيل الأصناف والعملاء والفواتير إلى دفتر الأستاذ</div>
        </div>
        <div style={{ display: 'flex', gap: 10 }}>
          <button type="button" className="btn-ghost" onClick={refresh}>↺ تحديث</button>
          <button type="button" className="btn-primary" disabled={syncing} onClick={() => void runSync()}>
            {syncing ? 'جارٍ البدء...' : '⇄ مزامنة الآن'}
          </button>
        </div>
      </div>

      {error ? <ErrorBanner message={error} /> : null}
      {info ? <div className="vex-card" style={{ marginBottom: 16, color: 'var(--clr-primary)' }}>{info}</div> : null}

      {(
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
              <h2 className="vex-section-title">آخر الأخطاء</h2>
              {failures.map((f, i) => (
                <div key={`${f.doctype}-${i}`} style={{ fontSize: 12, padding: '6px 0', borderTop: i ? '1px solid var(--clr-border)' : 'none' }}>
                  <strong>{doctypeLabels[f.doctype] ?? f.doctype}</strong>: {f.lastError ?? '-'}
                </div>
              ))}
            </div>
          ) : null}

          <div style={{ display: 'flex', gap: 8, marginBottom: 12, flexWrap: 'wrap' }}>
            {STATUS_FILTERS.map((f) => (
              <button key={f.key} type="button" onClick={() => setStatusFilter(f.key)} className={statusFilter === f.key ? 'btn-primary' : 'btn-ghost'} style={{ padding: '5px 14px', fontSize: 12 }}>{f.label}</button>
            ))}
          </div>

          <div className="vex-card vex-card--no-pad" style={{ opacity: loading ? 0.6 : 1, transition: 'opacity 120ms' }}>
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
            <Pagination page={list.page} pageSize={list.pageSize} totalCount={list.totalCount} onPageChange={list.setPage} onPageSizeChange={list.changePageSize} />
          </div>
        </>
      )}
    </div>
  );
}

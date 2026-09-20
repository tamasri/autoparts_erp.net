import { Fragment, useCallback, useState } from 'react';
import { cycleCountsApi } from '../../api/endpoints/cycleCounts';
import { unwrapNode } from '../../api/apiData';
import { usePagedList } from '../../hooks/usePagedList';
import { useLocationNames } from '../../hooks/useLocationNames';
import { toast, extractApiError } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';
import Pagination from '../../components/common/Pagination';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';
import StatusBadge from '../../components/common/StatusBadge';
import DocumentViewButton from '../../components/ui/DocumentViewButton';
import { cycleCountDocument } from '../../lib/wmsDocuments';
import LocationSelect from '../../components/pickers/LocationSelect';

type Plan = { id: string; warehouseId: string; scopeType: string; status: string; scheduledFor?: string };
type CountLine = { id: string; itemCode: string; itemName: string; locationCode: string; systemQty: number; countedQty: number | null; varianceQty: number };
type PlanDetail = Plan & { lines: CountLine[] };

const SCOPES = [
  { value: 'FULL', label: 'كامل' },
  { value: 'LOCATION', label: 'حسب الموقع' },
  { value: 'CATEGORY', label: 'حسب الفئة' },
  { value: 'ABC', label: 'تحليل ABC' },
];

const toLocalDate = (d: Date): string => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

export default function CycleCounts(): JSX.Element {
  const names = useLocationNames();
  const list = usePagedList<Plan>({
    errorMessage: 'تعذر تحميل خطط الجرد',
    fetcher: ({ page, pageSize }) => cycleCountsApi.list(page, pageSize),
  });

  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState('');
  const [formError, setFormError] = useState('');
  const [warehouseId, setWarehouseId] = useState('');
  const [scopeType, setScopeType] = useState('FULL');
  const [scheduledFor, setScheduledFor] = useState(toLocalDate(new Date()));

  const [openId, setOpenId] = useState('');
  const [detail, setDetail] = useState<PlanDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [counts, setCounts] = useState<Record<string, string>>({});
  const [actionError, setActionError] = useState('');

  const loadDetail = useCallback(async (id: string): Promise<void> => {
    setDetailLoading(true); setActionError('');
    try {
      const res = await cycleCountsApi.get(id);
      const d = unwrapNode<PlanDetail>(res.data);
      setDetail(d);
      setCounts(Object.fromEntries((d?.lines ?? []).map((l) => [l.id, l.countedQty === null ? '' : String(l.countedQty)])));
    } catch (e: unknown) { setActionError(extractApiError(e, 'تعذر تحميل أسطر الجرد')); }
    finally { setDetailLoading(false); }
  }, []);

  function togglePlan(id: string): void {
    if (openId === id) { setOpenId(''); return; }
    setOpenId(id);
    void loadDetail(id);
  }

  async function create(): Promise<void> {
    if (!warehouseId) { setFormError('اختر المستودع'); return; }
    setBusy('create'); setFormError('');
    try {
      await cycleCountsApi.create({ warehouseId, scopeType, scheduledFor });
      toast.success('تم إنشاء خطة الجرد وتوليد أسطرها من الأرصدة الحالية');
      setWarehouseId(''); setShowForm(false); list.reload();
    } catch (e: unknown) { setFormError(extractApiError(e, 'تعذر إنشاء خطة الجرد')); }
    finally { setBusy(''); }
  }

  async function submitCounts(): Promise<void> {
    if (!detail) return;
    const entered = detail.lines.filter((l) => counts[l.id] !== '' && counts[l.id] !== undefined);
    if (entered.length === 0) { setActionError('أدخل الكمية المعدودة لسطر واحد على الأقل'); return; }
    setBusy(detail.id); setActionError('');
    try {
      await cycleCountsApi.record(detail.id, entered.map((l) => ({ lineId: l.id, countedQty: Number(counts[l.id]) })));
      toast.success('تم تسجيل نتائج الجرد');
      list.reload(); await loadDetail(detail.id);
    } catch (e: unknown) { setActionError(extractApiError(e, 'تعذر تسجيل نتائج الجرد')); }
    finally { setBusy(''); }
  }

  async function approve(id: string): Promise<void> {
    setBusy(id);
    try { const res = await cycleCountsApi.approveVariance(id); notifyResult(res, 'تم اعتماد الفروقات وترحيلها'); list.reload(); setOpenId(''); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر اعتماد الفروقات')); }
    finally { setBusy(''); }
  }

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">الجرد الدوري</h1>
          <div className="vex-page-header__breadcrumb">خطة الجرد تُولَّد أسطرها من الأرصدة الحالية؛ أدخل الكميات المعدودة ثم اعتمد الفروقات</div>
        </div>
        <button type="button" onClick={() => setShowForm((s) => !s)} className={showForm ? 'btn-ghost' : 'btn-primary'}>
          {showForm ? '✕ إلغاء' : '＋ خطة جرد'}
        </button>
      </div>

      {list.error ? <ErrorBanner message={list.error} /> : null}

      {showForm ? (
        <div className="vex-card" style={{ marginBottom: 20 }}>
          <h2 className="vex-section-title">خطة جرد جديدة</h2>
          {formError ? <ErrorBanner message={formError} /> : null}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 16, marginBottom: 20 }}>
            <label className="vex-label">المستودع *<LocationSelect type="WAREHOUSE" value={warehouseId} onChange={(id) => setWarehouseId(id)} /></label>
            <label className="vex-label">نطاق الجرد
              <select value={scopeType} onChange={(e) => setScopeType(e.target.value)} className="vex-select">
                {SCOPES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
              </select>
            </label>
            <label className="vex-label">تاريخ التنفيذ<input type="date" value={scheduledFor} onChange={(e) => setScheduledFor(e.target.value)} className="vex-input" /></label>
          </div>
          <button type="button" disabled={busy === 'create'} onClick={() => void create()} className="btn-primary">💾 حفظ الخطة</button>
        </div>
      ) : null}

      <div className="vex-card vex-card--no-pad" style={{ opacity: list.loading ? 0.6 : 1 }}>
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead><tr><th>المستودع</th><th>النطاق</th><th>التاريخ</th><th>الحالة</th><th>إجراءات</th></tr></thead>
            <tbody>
              {list.items.length === 0 ? (
                <tr><td colSpan={5} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>لا توجد خطط جرد</td></tr>
              ) : list.items.map((p) => (
                <Fragment key={p.id}>
                  <tr>
                    <td style={{ fontWeight: 600 }}>{names.label(p.warehouseId)}</td>
                    <td>{SCOPES.find((s) => s.value === p.scopeType)?.label ?? p.scopeType}</td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{p.scheduledFor ?? '-'}</td>
                    <td><StatusBadge status={p.status} type="invoice" /></td>
                    <td style={{ whiteSpace: 'nowrap' }}>
                      <DocumentViewButton load={() => cycleCountDocument(p.id, names.label)} />{' '}
                      <button type="button" onClick={() => togglePlan(p.id)} className="btn-secondary" style={{ padding: '5px 12px', fontSize: 12, marginLeft: 6 }}>
                        {openId === p.id ? '▲ إخفاء' : '📋 أسطر الجرد'}
                      </button>
                      {p.status === 'PENDING_APPROVAL' ? (
                        <button type="button" disabled={busy === p.id} onClick={() => void approve(p.id)} className="btn-success" style={{ padding: '5px 12px', fontSize: 12 }}>✓ اعتماد الفروقات</button>
                      ) : null}
                    </td>
                  </tr>
                  {openId === p.id ? (
                    <tr>
                      <td colSpan={5} style={{ background: 'var(--clr-surface-2)', padding: '14px 20px' }}>
                        {actionError ? <ErrorBanner message={actionError} /> : null}
                        {detailLoading || !detail ? <LoadingSpinner /> : (
                          <>
                            <table className="vex-table" style={{ background: '#fff' }}>
                              <thead><tr><th>الصنف</th><th>الموقع</th><th>رصيد النظام</th><th style={{ width: 130 }}>الكمية المعدودة</th><th>الفرق</th></tr></thead>
                              <tbody>
                                {detail.lines.length === 0 ? (
                                  <tr><td colSpan={5} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: 20 }}>لا توجد أرصدة في هذا المستودع لتُجرد</td></tr>
                                ) : detail.lines.map((l) => {
                                  const raw = counts[l.id];
                                  const variance = raw === '' || raw === undefined ? null : Number(raw) - l.systemQty;
                                  return (
                                    <tr key={l.id}>
                                      <td><span style={{ fontFamily: 'monospace', fontWeight: 700, color: 'var(--clr-primary)' }}>{l.itemCode}</span> <span style={{ color: 'var(--txt-secondary)' }}>{l.itemName}</span></td>
                                      <td>{l.locationCode}</td>
                                      <td>{l.systemQty.toLocaleString('en-US')}</td>
                                      <td><input type="number" min={0} className="vex-input" value={raw ?? ''} disabled={detail.status === 'POSTED'} onChange={(e) => setCounts({ ...counts, [l.id]: e.target.value })} /></td>
                                      <td style={{ fontWeight: 700, color: variance === null || variance === 0 ? 'var(--txt-muted)' : variance > 0 ? 'var(--clr-success)' : 'var(--clr-danger)' }}>
                                        {variance === null ? '—' : (variance > 0 ? '+' : '') + variance.toLocaleString('en-US')}
                                      </td>
                                    </tr>
                                  );
                                })}
                              </tbody>
                            </table>
                            {detail.status !== 'POSTED' && detail.lines.length > 0 ? (
                              <div style={{ marginTop: 12 }}>
                                <button type="button" disabled={busy === detail.id} onClick={() => void submitCounts()} className="btn-primary">📤 حفظ نتائج الجرد</button>
                              </div>
                            ) : null}
                          </>
                        )}
                      </td>
                    </tr>
                  ) : null}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
        <Pagination page={list.page} pageSize={list.pageSize} totalCount={list.totalCount} onPageChange={list.setPage} onPageSizeChange={list.changePageSize} />
      </div>
    </div>
  );
}

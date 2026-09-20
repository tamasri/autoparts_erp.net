import { Fragment, useCallback, useState } from 'react';
import { issueOrdersApi } from '../../api/endpoints/issueOrders';
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
import { issueOrderDocument } from '../../lib/wmsDocuments';
import LocationSelect from '../../components/pickers/LocationSelect';
import WmsLinesEditor, { type WmsLine } from '../../components/wms/WmsLinesEditor';

type IssueOrder = { id: string; orderNo: string; sourceType: string; warehouseId: string; status: string; issuedAt?: string };
type OrderLine = { id: string; itemCode: string; itemName: string; requestedQty: number; pickedQty: number; verifiedQty: number; issuedQty: number };
type PickTask = { id: string; itemCode: string; itemName: string; locationCode: string; qty: number; status: string };
type OrderDetail = { order: IssueOrder; lines: OrderLine[]; pickTasks: PickTask[] };

const SOURCES = [
  { value: 'MANUAL', label: 'يدوي' },
  { value: 'SALES_ORDER', label: 'أمر بيع' },
  { value: 'TRANSFER', label: 'تحويل' },
];

export default function IssueOrders(): JSX.Element {
  const names = useLocationNames();
  const list = usePagedList<IssueOrder>({
    errorMessage: 'تعذر تحميل أوامر الصرف',
    fetcher: ({ page, pageSize }) => issueOrdersApi.list(page, pageSize),
  });

  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState('');
  const [formError, setFormError] = useState('');
  const [sourceType, setSourceType] = useState('MANUAL');
  const [warehouseId, setWarehouseId] = useState('');
  const [lines, setLines] = useState<WmsLine[]>([]);

  const [openId, setOpenId] = useState('');
  const [detail, setDetail] = useState<OrderDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [actionError, setActionError] = useState('');

  const loadDetail = useCallback(async (id: string): Promise<void> => {
    setDetailLoading(true); setActionError('');
    try { const res = await issueOrdersApi.get(id); setDetail(unwrapNode<OrderDetail>(res.data)); }
    catch (e: unknown) { setActionError(extractApiError(e, 'تعذر تحميل تفاصيل الأمر')); }
    finally { setDetailLoading(false); }
  }, []);

  function toggle(id: string): void {
    if (openId === id) { setOpenId(''); return; }
    setOpenId(id);
    void loadDetail(id);
  }

  async function create(): Promise<void> {
    if (!warehouseId) { setFormError('اختر المستودع'); return; }
    const valid = lines.filter((l) => Number(l.qty) > 0);
    if (valid.length === 0) { setFormError('أضف صنفاً واحداً على الأقل بكمية صحيحة'); return; }
    const over = valid.find((l) => Number(l.qty) > (l.item.stock.find((s) => s.locationId === l.locationId)?.available ?? 0));
    if (over) { setFormError(`الكمية المطلوبة تتجاوز المتاح للصنف ${over.code}`); return; }
    setBusy('create'); setFormError('');
    try {
      await issueOrdersApi.create({
        sourceType,
        warehouseId,
        lines: valid.map((l) => ({ itemId: l.itemId, requestedQty: Number(l.qty), sourceLocationId: l.locationId })),
        idempotencyKey: crypto.randomUUID(),
      });
      toast.success('تم إنشاء أمر الصرف');
      setWarehouseId(''); setLines([]); setShowForm(false); list.reload();
    } catch (e: unknown) { setFormError(extractApiError(e, 'تعذر إنشاء أمر الصرف')); }
    finally { setBusy(''); }
  }

  async function step(id: string, run: () => Promise<{ data: unknown }>, done: string, fail: string): Promise<void> {
    setBusy(id); setActionError('');
    try {
      const res = await run();
      notifyResult(res as never, done);
      await loadDetail(id);
      list.reload();
    } catch (e: unknown) { setActionError(extractApiError(e, fail)); }
    finally { setBusy(''); }
  }

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">أوامر الصرف</h1>
          <div className="vex-page-header__breadcrumb">أمر ← مهام سحب ← تحقق ← صرف من المخزون</div>
        </div>
        <button type="button" onClick={() => setShowForm((s) => !s)} className={showForm ? 'btn-ghost' : 'btn-primary'}>
          {showForm ? '✕ إلغاء' : '＋ أمر صرف'}
        </button>
      </div>

      {list.error ? <ErrorBanner message={list.error} /> : null}

      {showForm ? (
        <div className="vex-card" style={{ marginBottom: 20 }}>
          <h2 className="vex-section-title">أمر صرف جديد</h2>
          {formError ? <ErrorBanner message={formError} /> : null}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 16, marginBottom: 20 }}>
            <label className="vex-label">نوع المصدر
              <select value={sourceType} onChange={(e) => setSourceType(e.target.value)} className="vex-select">
                {SOURCES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
              </select>
            </label>
            <label className="vex-label">المستودع *<LocationSelect type="WAREHOUSE" value={warehouseId} onChange={(id) => setWarehouseId(id)} /></label>
          </div>
          <WmsLinesEditor lines={lines} onChange={setLines} warehouseId={warehouseId} locationLabel="السحب من موقع" qtyLabel="الكمية المطلوبة" showAvailable pickerTitle="اختيار الأصناف المراد صرفها" />
          <div style={{ marginTop: 14 }}>
            <button type="button" disabled={busy === 'create'} onClick={() => void create()} className="btn-primary">💾 حفظ الأمر</button>
          </div>
        </div>
      ) : null}

      <div className="vex-card vex-card--no-pad" style={{ opacity: list.loading ? 0.6 : 1 }}>
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead><tr><th>رقم الأمر</th><th>المصدر</th><th>المستودع</th><th>الحالة</th><th>إجراءات</th></tr></thead>
            <tbody>
              {list.items.length === 0 ? (
                <tr><td colSpan={5} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>لا توجد أوامر صرف</td></tr>
              ) : list.items.map((o) => (
                <Fragment key={o.id}>
                  <tr>
                    <td style={{ fontWeight: 600, color: 'var(--clr-primary)' }}>{o.orderNo}</td>
                    <td>{SOURCES.find((s) => s.value === o.sourceType)?.label ?? o.sourceType}</td>
                    <td>{names.label(o.warehouseId)}</td>
                    <td><StatusBadge status={o.status} type="invoice" /></td>
                    <td style={{ whiteSpace: 'nowrap' }}><DocumentViewButton load={() => issueOrderDocument(o.id, names.label)} />{' '}<button type="button" className="btn-secondary" style={{ padding: '5px 12px', fontSize: 12 }} onClick={() => toggle(o.id)}>{openId === o.id ? '▲ إخفاء' : '▼ التفاصيل والسحب'}</button></td>
                  </tr>
                  {openId === o.id ? (
                    <tr>
                      <td colSpan={5} style={{ background: 'var(--clr-surface-2)', padding: '14px 20px' }}>
                        {actionError ? <ErrorBanner message={actionError} /> : null}
                        {detailLoading || !detail ? <LoadingSpinner /> : (
                          <>
                            <table className="vex-table" style={{ background: '#fff', marginBottom: 12 }}>
                              <thead><tr><th>الصنف</th><th>مطلوب</th><th>مسحوب</th><th>مُتحقَّق</th><th>مصروف</th></tr></thead>
                              <tbody>
                                {detail.lines.map((l) => (
                                  <tr key={l.id}>
                                    <td><span style={{ fontFamily: 'monospace', fontWeight: 700, color: 'var(--clr-primary)' }}>{l.itemCode}</span> {l.itemName}</td>
                                    <td>{l.requestedQty}</td><td>{l.pickedQty}</td><td>{l.verifiedQty}</td><td>{l.issuedQty}</td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>

                            {detail.pickTasks.length > 0 ? (
                              <div style={{ marginBottom: 12 }}>
                                <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--txt-muted)', marginBottom: 6 }}>مهام السحب</div>
                                {detail.pickTasks.map((t) => (
                                  <div key={t.id} style={{ display: 'flex', gap: 14, alignItems: 'center', padding: '8px 12px', marginBottom: 6, background: '#fff', borderRadius: 'var(--radius-md)', border: '1px solid var(--clr-border)', flexWrap: 'wrap' }}>
                                    <span style={{ fontFamily: 'monospace', fontWeight: 700 }}>{t.itemCode}</span>
                                    <span style={{ fontSize: 13 }}>من <strong>{t.locationCode}</strong> · الكمية <strong>{t.qty}</strong></span>
                                    <StatusBadge status={t.status} type="invoice" />
                                    {t.status === 'PENDING' ? <button type="button" disabled={busy === t.id} className="btn-primary" style={{ padding: '4px 12px', fontSize: 12 }} onClick={() => void step(o.id, () => issueOrdersApi.completePick(o.id, t.id), 'تم السحب', 'تعذر إتمام السحب')}>تم السحب</button> : null}
                                    {t.status === 'PICKED' ? <button type="button" disabled={busy === t.id} className="btn-success" style={{ padding: '4px 12px', fontSize: 12 }} onClick={() => void step(o.id, () => issueOrdersApi.verifyPick(o.id, t.id), 'تم التحقق', 'تعذر التحقق')}>تحقّق</button> : null}
                                  </div>
                                ))}
                              </div>
                            ) : null}

                            <div style={{ display: 'flex', gap: 10 }}>
                              {detail.order.status === 'DRAFT' ? (
                                <button type="button" disabled={busy === o.id} className="btn-primary" onClick={() => void step(o.id, () => issueOrdersApi.generatePickTasks(o.id), 'تم توليد مهام السحب', 'تعذر توليد المهام')}>⚙ توليد مهام السحب</button>
                              ) : null}
                              {detail.order.status !== 'ISSUED' && detail.pickTasks.length > 0 && detail.pickTasks.every((t) => t.status === 'VERIFIED') ? (
                                <button type="button" disabled={busy === o.id} className="btn-success" onClick={() => void step(o.id, () => issueOrdersApi.issue(o.id), 'تم صرف الأمر من المخزون', 'تعذر صرف الأمر')}>✓ صرف من المخزون</button>
                              ) : null}
                            </div>
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

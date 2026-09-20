import { Fragment, useCallback, useState } from 'react';
import { receivingApi, type ReceivingLine } from '../../api/endpoints/receiving';
import { partiesApi } from '../../api/endpoints/parties';
import { unwrapList, unwrapNode, unwrapPaged } from '../../api/apiData';
import { usePagedList } from '../../hooks/usePagedList';
import { useLocationNames } from '../../hooks/useLocationNames';
import { toast, extractApiError } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';
import Pagination from '../../components/common/Pagination';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';
import StatusBadge from '../../components/common/StatusBadge';
import EntityPicker, { type PickerOption } from '../../components/pickers/EntityPicker';
import DocumentViewButton from '../../components/ui/DocumentViewButton';
import { receivingDocument } from '../../lib/wmsDocuments';
import LocationSelect from '../../components/pickers/LocationSelect';
import WmsLinesEditor, { type WmsLine } from '../../components/wms/WmsLinesEditor';

type ReceivingDoc = { id: string; documentNo: string; vendorPartyId?: string; purchaseOrderRef?: string; warehouseId: string; status: string; postedAt?: string };
type DocLine = { id: string; itemCode: string; itemName: string; expectedQty?: number; receivedQty: number; rejectedQty: number; assignedLocationId?: string; conditionStatus: string };
type DocDetail = ReceivingDoc & { notes?: string; lines: DocLine[] };
type PutawayTask = { id: string; qty: number; status: string; toLocationId: string };
type PartyRow = { id: string; displayNameAr?: string; displayName?: string };

const CONDITIONS = [
  { value: 'GOOD', label: 'سليم' },
  { value: 'DAMAGED', label: 'تالف' },
  { value: 'PARTIAL', label: 'جزئي' },
];

const conditionLabel = (c: string): string => CONDITIONS.find((x) => x.value === c)?.label ?? c;

function toApiLines(lines: WmsLine[]): ReceivingLine[] {
  return lines.filter((l) => Number(l.qty) > 0).map((l) => ({
    itemId: l.itemId,
    receivedQty: Number(l.qty),
    rejectedQty: Number(l.extra.rejected ?? 0),
    expectedQty: l.extra.expected === '' || l.extra.expected === undefined ? undefined : Number(l.extra.expected),
    assignedLocationId: l.locationId || undefined,
    conditionStatus: String(l.extra.condition ?? 'GOOD'),
  }));
}

function ReceivingLinesEditor({ lines, onChange, warehouseId }: { lines: WmsLine[]; onChange: (l: WmsLine[]) => void; warehouseId: string }): JSX.Element {
  return (
    <WmsLinesEditor
      lines={lines}
      onChange={onChange}
      warehouseId={warehouseId}
      locationLabel="موقع التخزين"
      qtyLabel="الكمية المستلمة"
      showAvailable={false}
      defaultExtra={() => ({ expected: '', rejected: 0, condition: 'GOOD' })}
      pickerTitle="اختيار الأصناف المستلمة"
      extraColumns={[
        { key: 'expected', label: 'المتوقعة', width: 100, render: (l, patch) => <input type="number" min={0} className="vex-input" value={l.extra.expected ?? ''} onChange={(e) => patch({ expected: e.target.value })} /> },
        { key: 'rejected', label: 'المرفوضة', width: 100, render: (l, patch) => <input type="number" min={0} className="vex-input" value={Number(l.extra.rejected ?? 0)} onChange={(e) => patch({ rejected: Number(e.target.value) })} /> },
        {
          key: 'condition', label: 'الحالة', width: 120,
          render: (l, patch) => (
            <select className="vex-select" value={String(l.extra.condition ?? 'GOOD')} onChange={(e) => patch({ condition: e.target.value })}>
              {CONDITIONS.map((c) => <option key={c.value} value={c.value}>{c.label}</option>)}
            </select>
          ),
        },
      ]}
    />
  );
}

export default function Receiving(): JSX.Element {
  const names = useLocationNames();
  const list = usePagedList<ReceivingDoc>({
    errorMessage: 'تعذر تحميل مستندات الاستلام',
    fetcher: ({ page, pageSize }) => receivingApi.list(page, pageSize),
  });

  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState('');
  const [formError, setFormError] = useState('');
  const [warehouseId, setWarehouseId] = useState('');
  const [vendor, setVendor] = useState<PickerOption | null>(null);
  const [poRef, setPoRef] = useState('');
  const [notes, setNotes] = useState('');
  const [lines, setLines] = useState<WmsLine[]>([]);

  const [openId, setOpenId] = useState('');
  const [detail, setDetail] = useState<DocDetail | null>(null);
  const [tasks, setTasks] = useState<PutawayTask[]>([]);
  const [taskTargets, setTaskTargets] = useState<Record<string, string>>({});
  const [detailLoading, setDetailLoading] = useState(false);
  const [actionError, setActionError] = useState('');
  const [extraLines, setExtraLines] = useState<WmsLine[]>([]);

  const searchVendors = useCallback(async (text: string): Promise<PickerOption[]> => {
    const res = await partiesApi.getParties({ page: 1, pageSize: 10, typeCode: 'VENDOR', isActive: true, searchTerm: text || undefined });
    return unwrapPaged<PartyRow>(res.data).items.map((p) => ({ id: p.id, label: p.displayNameAr || p.displayName || p.id.slice(0, 8), sublabel: p.displayName }));
  }, []);

  const loadDetail = useCallback(async (id: string): Promise<void> => {
    setDetailLoading(true); setActionError('');
    try {
      const [d, t] = await Promise.all([receivingApi.get(id), receivingApi.getPutawayTasks(id)]);
      const doc = unwrapNode<DocDetail>(d.data);
      const rows = unwrapList<PutawayTask>(t.data);
      setDetail(doc); setTasks(rows);
      setTaskTargets(Object.fromEntries(rows.map((x) => [x.id, x.toLocationId])));
    } catch (e: unknown) { setActionError(extractApiError(e, 'تعذر تحميل تفاصيل المستند')); }
    finally { setDetailLoading(false); }
  }, []);

  function toggle(id: string): void {
    if (openId === id) { setOpenId(''); return; }
    setOpenId(id); setExtraLines([]);
    void loadDetail(id);
  }

  async function create(): Promise<void> {
    if (!warehouseId) { setFormError('اختر المستودع'); return; }
    setBusy('create'); setFormError('');
    try {
      const res = await receivingApi.create({ warehouseId, vendorPartyId: vendor?.id, purchaseOrderRef: poRef.trim() || undefined, notes: notes.trim() || undefined });
      const created = unwrapNode<{ id: string }>(res.data);
      const apiLines = toApiLines(lines);
      if (created?.id) for (const l of apiLines) await receivingApi.addLine(created.id, l);
      toast.success('تم إنشاء مستند الاستلام');
      setWarehouseId(''); setVendor(null); setPoRef(''); setNotes(''); setLines([]); setShowForm(false); list.reload();
    } catch (e: unknown) { setFormError(extractApiError(e, 'تعذر إنشاء المستند')); }
    finally { setBusy(''); }
  }

  async function addLines(id: string): Promise<void> {
    const apiLines = toApiLines(extraLines);
    if (apiLines.length === 0) { setActionError('أضف صنفاً واحداً على الأقل بكمية مستلمة'); return; }
    setBusy(id); setActionError('');
    try {
      for (const l of apiLines) await receivingApi.addLine(id, l);
      toast.success('تمت إضافة الأسطر');
      setExtraLines([]); await loadDetail(id);
    } catch (e: unknown) { setActionError(extractApiError(e, 'تعذر إضافة الأسطر')); }
    finally { setBusy(''); }
  }

  async function post(id: string): Promise<void> {
    setBusy(id); setActionError('');
    try { const res = await receivingApi.post(id); notifyResult(res, 'تم ترحيل مستند الاستلام'); list.reload(); await loadDetail(id); }
    catch (e: unknown) { setActionError(extractApiError(e, 'تعذر ترحيل المستند')); }
    finally { setBusy(''); }
  }

  async function completeTask(docId: string, t: PutawayTask): Promise<void> {
    const to = taskTargets[t.id];
    if (!to) { setActionError('اختر موقع التخزين'); return; }
    setBusy(t.id); setActionError('');
    try { await receivingApi.completePutaway(t.id, { toLocationId: to, qty: t.qty }); toast.success('تم التخزين'); await loadDetail(docId); }
    catch (e: unknown) { setActionError(extractApiError(e, 'تعذر إتمام مهمة التخزين')); }
    finally { setBusy(''); }
  }

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">الاستلام والتخزين</h1>
          <div className="vex-page-header__breadcrumb">استلام البضاعة من الموردين وتخزينها</div>
        </div>
        <button type="button" onClick={() => setShowForm((s) => !s)} className={showForm ? 'btn-ghost' : 'btn-primary'}>
          {showForm ? '✕ إلغاء' : '＋ مستند استلام'}
        </button>
      </div>

      {list.error ? <ErrorBanner message={list.error} /> : null}

      {showForm ? (
        <div className="vex-card" style={{ marginBottom: 20 }}>
          <h2 className="vex-section-title">مستند استلام جديد</h2>
          {formError ? <ErrorBanner message={formError} /> : null}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: 16, marginBottom: 20 }}>
            <label className="vex-label">المستودع *<LocationSelect type="WAREHOUSE" value={warehouseId} onChange={(id) => setWarehouseId(id)} /></label>
            <label className="vex-label">المورّد<EntityPicker value={vendor} onChange={setVendor} search={searchVendors} placeholder="ابحث باسم المورّد..." /></label>
            <label className="vex-label">مرجع أمر الشراء<input value={poRef} onChange={(e) => setPoRef(e.target.value)} className="vex-input" /></label>
            <label className="vex-label">ملاحظات<input value={notes} onChange={(e) => setNotes(e.target.value)} className="vex-input" /></label>
          </div>
          <ReceivingLinesEditor lines={lines} onChange={setLines} warehouseId={warehouseId} />
          <div style={{ marginTop: 14 }}>
            <button type="button" disabled={busy === 'create'} onClick={() => void create()} className="btn-primary">💾 حفظ المستند</button>
          </div>
        </div>
      ) : null}

      <div className="vex-card vex-card--no-pad" style={{ opacity: list.loading ? 0.6 : 1 }}>
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead><tr><th>رقم المستند</th><th>المستودع</th><th>مرجع الشراء</th><th>الحالة</th><th>تاريخ الترحيل</th><th>إجراءات</th></tr></thead>
            <tbody>
              {list.items.length === 0 ? (
                <tr><td colSpan={6} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>لا توجد مستندات</td></tr>
              ) : list.items.map((d) => (
                <Fragment key={d.id}>
                  <tr>
                    <td style={{ fontWeight: 600, color: 'var(--clr-primary)' }}>{d.documentNo}</td>
                    <td>{names.label(d.warehouseId)}</td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{d.purchaseOrderRef || '-'}</td>
                    <td><StatusBadge status={d.status} type="invoice" /></td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{d.postedAt ? new Date(d.postedAt).toLocaleDateString('ar') : '-'}</td>
                    <td style={{ whiteSpace: 'nowrap' }}><DocumentViewButton load={() => receivingDocument(d.id, names.label)} />{' '}<button type="button" onClick={() => toggle(d.id)} className="btn-secondary" style={{ padding: '5px 14px', fontSize: 12 }}>{openId === d.id ? '▲ إخفاء' : '▼ الأسطر والتخزين'}</button></td>
                  </tr>
                  {openId === d.id ? (
                    <tr>
                      <td colSpan={6} style={{ background: 'var(--clr-surface-2)', padding: '14px 20px' }}>
                        {actionError ? <ErrorBanner message={actionError} /> : null}
                        {detailLoading || !detail ? <LoadingSpinner /> : (
                          <>
                            <table className="vex-table" style={{ background: '#fff', marginBottom: 12 }}>
                              <thead><tr><th>الصنف</th><th>المتوقعة</th><th>المستلمة</th><th>المرفوضة</th><th>الحالة</th><th>موقع التخزين</th></tr></thead>
                              <tbody>
                                {detail.lines.length === 0 ? (
                                  <tr><td colSpan={6} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: 16 }}>لا توجد أسطر بعد</td></tr>
                                ) : detail.lines.map((l) => (
                                  <tr key={l.id}>
                                    <td><span style={{ fontFamily: 'monospace', fontWeight: 700, color: 'var(--clr-primary)' }}>{l.itemCode}</span> {l.itemName}</td>
                                    <td>{l.expectedQty ?? '-'}</td><td>{l.receivedQty}</td><td>{l.rejectedQty}</td>
                                    <td>{conditionLabel(l.conditionStatus)}</td>
                                    <td>{l.assignedLocationId ? names.label(l.assignedLocationId) : '-'}</td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>

                            {detail.status !== 'POSTED' ? (
                              <div style={{ marginBottom: 12 }}>
                                <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--txt-muted)', marginBottom: 6 }}>إضافة أسطر</div>
                                <ReceivingLinesEditor lines={extraLines} onChange={setExtraLines} warehouseId={detail.warehouseId} />
                                <div style={{ display: 'flex', gap: 10, marginTop: 10 }}>
                                  <button type="button" disabled={busy === d.id} className="btn-secondary" onClick={() => void addLines(d.id)}>＋ إضافة للمستند</button>
                                  <button type="button" disabled={busy === d.id || detail.lines.length === 0} className="btn-success" onClick={() => void post(d.id)}>✓ ترحيل المستند</button>
                                </div>
                              </div>
                            ) : null}

                            {tasks.length > 0 ? (
                              <div>
                                <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--txt-muted)', marginBottom: 6 }}>مهام التخزين</div>
                                {tasks.map((t) => (
                                  <div key={t.id} style={{ display: 'flex', gap: 14, alignItems: 'center', padding: '8px 12px', marginBottom: 6, background: '#fff', borderRadius: 'var(--radius-md)', border: '1px solid var(--clr-border)', flexWrap: 'wrap' }}>
                                    <span style={{ fontSize: 13 }}>الكمية: <strong>{t.qty}</strong></span>
                                    <StatusBadge status={t.status} type="invoice" />
                                    {t.status !== 'COMPLETED' ? (
                                      <>
                                        <div style={{ minWidth: 220 }}>
                                          <LocationSelect value={taskTargets[t.id] ?? ''} allowEmpty={false} onChange={(id) => setTaskTargets({ ...taskTargets, [t.id]: id })} />
                                        </div>
                                        <button type="button" disabled={busy === t.id} onClick={() => void completeTask(d.id, t)} className="btn-primary" style={{ padding: '4px 12px', fontSize: 12 }}>إتمام التخزين</button>
                                      </>
                                    ) : null}
                                  </div>
                                ))}
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

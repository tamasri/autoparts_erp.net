import { useState } from 'react';
import { stockAdjustmentsApi } from '../../api/endpoints/stockAdjustments';
import { usePagedList } from '../../hooks/usePagedList';
import { useLocationNames } from '../../hooks/useLocationNames';
import { toast, extractApiError } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';
import Pagination from '../../components/common/Pagination';
import ErrorBanner from '../../components/common/ErrorBanner';
import StatusBadge from '../../components/common/StatusBadge';
import LocationSelect from '../../components/pickers/LocationSelect';
import ReasonCodeSelect from '../../components/pickers/ReasonCodeSelect';
import WmsLinesEditor, { type WmsLine } from '../../components/wms/WmsLinesEditor';

type StockAdjustment = {
  id: string;
  adjustmentNo: string;
  adjustmentType: string;
  warehouseId: string;
  reasonCode: string;
  status: string;
  postedAt?: string;
};

// Values allowed by the database (stock_adjustments_adjustment_type_check).
const TYPES: { value: string; label: string; badge: string }[] = [
  { value: 'MANUAL', label: 'تسوية يدوية', badge: 'badge--warning' },
  { value: 'DAMAGE', label: 'تالف', badge: 'badge--danger' },
  { value: 'FOUND', label: 'زيادة (عُثر عليها)', badge: 'badge--success' },
  { value: 'CYCLE_COUNT', label: 'نتيجة جرد', badge: 'badge--draft' },
];

const systemBefore = (line: WmsLine, locationId: string): number => line.item.stock.find((s) => s.locationId === locationId)?.available ?? 0;

export default function StockAdjustments(): JSX.Element {
  const names = useLocationNames();
  const list = usePagedList<StockAdjustment>({
    errorMessage: 'تعذر تحميل تسويات المخزون',
    fetcher: ({ page, pageSize }) => stockAdjustmentsApi.list(page, pageSize),
  });

  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState('');
  const [formError, setFormError] = useState('');
  const [adjustmentType, setAdjustmentType] = useState('MANUAL');
  const [warehouseId, setWarehouseId] = useState('');
  const [reasonCode, setReasonCode] = useState('');
  const [lines, setLines] = useState<WmsLine[]>([]);

  // "extra.delta" is the signed change; the quantity before is read from the item's real stock at that location.
  const deltaOf = (l: WmsLine): number => Number(l.extra.delta ?? 0);

  async function create(): Promise<void> {
    if (!warehouseId) { setFormError('اختر المستودع'); return; }
    if (!reasonCode) { setFormError('اختر سبب التسوية'); return; }
    const valid = lines.filter((l) => deltaOf(l) !== 0);
    if (valid.length === 0) { setFormError('أضف صنفاً واحداً على الأقل بمقدار تغيير غير صفري'); return; }
    const negative = valid.find((l) => systemBefore(l, l.locationId) + deltaOf(l) < 0);
    if (negative) { setFormError(`النتيجة سالبة للصنف ${negative.code} — لا يمكن أن ينزل الرصيد عن الصفر`); return; }
    setBusy('create'); setFormError('');
    try {
      await stockAdjustmentsApi.create({
        adjustmentType,
        warehouseId,
        reasonCode,
        lines: valid.map((l) => {
          const before = systemBefore(l, l.locationId);
          return { itemId: l.itemId, locationId: l.locationId, status: 'AVAILABLE', qtyDelta: deltaOf(l), systemQtyBefore: before, systemQtyAfter: before + deltaOf(l) };
        }),
      });
      toast.success('تم إنشاء التسوية (مسودة)');
      setWarehouseId(''); setReasonCode(''); setLines([]); setShowForm(false); list.reload();
    } catch (e: unknown) { setFormError(extractApiError(e, 'تعذر إنشاء التسوية')); }
    finally { setBusy(''); }
  }

  async function post(id: string): Promise<void> {
    setBusy(id);
    try { const res = await stockAdjustmentsApi.post(id); notifyResult(res, 'تم ترحيل التسوية'); list.reload(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر ترحيل التسوية')); }
    finally { setBusy(''); }
  }

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">تسويات المخزون</h1>
          <div className="vex-page-header__breadcrumb">تعديل أرصدة المخزون وتسوية الفروقات</div>
        </div>
        <button type="button" onClick={() => setShowForm((s) => !s)} className={showForm ? 'btn-ghost' : 'btn-primary'}>
          {showForm ? '✕ إلغاء' : '＋ تسوية جديدة'}
        </button>
      </div>

      {list.error ? <ErrorBanner message={list.error} /> : null}

      {showForm ? (
        <div className="vex-card" style={{ marginBottom: 20 }}>
          <h2 className="vex-section-title">تسوية مخزون جديدة</h2>
          {formError ? <ErrorBanner message={formError} /> : null}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 16, marginBottom: 20 }}>
            <label className="vex-label">نوع التسوية
              <select value={adjustmentType} onChange={(e) => setAdjustmentType(e.target.value)} className="vex-select">
                {TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
              </select>
            </label>
            <label className="vex-label">المستودع *
              <LocationSelect type="WAREHOUSE" value={warehouseId} onChange={(id) => setWarehouseId(id)} />
            </label>
            <label className="vex-label">سبب التسوية *
              <ReasonCodeSelect category="STOCK_ADJUST" value={reasonCode} onChange={setReasonCode} />
            </label>
          </div>

          <WmsLinesEditor
            lines={lines}
            onChange={setLines}
            warehouseId={warehouseId}
            qtyLabel="الرصيد الحالي"
            showAvailable={false}
            defaultExtra={() => ({ delta: 0 })}
            pickerTitle="اختيار أصناف التسوية"
            extraColumns={[
              {
                key: 'delta', label: 'مقدار التغيير (+/−)', width: 140,
                render: (l, patch) => <input type="number" className="vex-input" value={Number(l.extra.delta ?? 0)} onChange={(e) => patch({ delta: Number(e.target.value) })} />,
              },
              {
                key: 'after', label: 'الناتج', width: 110,
                render: (l) => {
                  const after = systemBefore(l, l.locationId) + deltaOf(l);
                  return <strong style={{ color: after >= 0 ? 'var(--clr-success)' : 'var(--clr-danger)' }}>{after.toLocaleString('en-US')}</strong>;
                },
              },
            ]}
          />
          <div style={{ marginTop: 14 }}>
            <button type="button" disabled={busy === 'create'} onClick={() => void create()} className="btn-primary">💾 حفظ التسوية</button>
          </div>
        </div>
      ) : null}

      <div className="vex-card vex-card--no-pad" style={{ opacity: list.loading ? 0.6 : 1 }}>
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead><tr><th>رقم التسوية</th><th>النوع</th><th>المستودع</th><th>السبب</th><th>الحالة</th><th>إجراءات</th></tr></thead>
            <tbody>
              {list.items.length === 0 ? (
                <tr><td colSpan={6} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>لا توجد تسويات</td></tr>
              ) : list.items.map((a) => {
                const t = TYPES.find((x) => x.value === a.adjustmentType);
                return (
                  <tr key={a.id}>
                    <td style={{ fontWeight: 600, color: 'var(--clr-primary)' }}>{a.adjustmentNo}</td>
                    <td><span className={`badge ${t?.badge ?? 'badge--draft'}`}>{t?.label ?? a.adjustmentType}</span></td>
                    <td>{names.label(a.warehouseId)}</td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{a.reasonCode}</td>
                    <td><StatusBadge status={a.status} type="invoice" /></td>
                    <td>
                      {a.status !== 'POSTED' ? (
                        <button type="button" disabled={busy === a.id} onClick={() => void post(a.id)} className="btn-success" style={{ padding: '5px 14px', fontSize: 12 }}>✓ ترحيل</button>
                      ) : <span className="badge badge--success">مرحّل</span>}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <Pagination page={list.page} pageSize={list.pageSize} totalCount={list.totalCount} onPageChange={list.setPage} onPageSizeChange={list.changePageSize} />
      </div>
    </div>
  );
}

import { useEffect, useState } from 'react';
import { stockAdjustmentsApi, type CreateStockAdjustment, type StockAdjustmentLine } from '../../api/endpoints/stockAdjustments';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';
import StatusBadge from '../../components/common/StatusBadge';

type StockAdjustment = {
  id: string;
  adjustmentNo: string;
  adjustmentType: string;
  warehouseId: string;
  reasonCode: string;
  status: string;
  postedAt?: string;
};

const emptyLine: StockAdjustmentLine = { itemId: '', locationId: '', status: 'AVAILABLE', qtyDelta: 0, systemQtyBefore: 0, systemQtyAfter: 0 };

function extractError(e: unknown, fallback: string): string {
  const r = e as { response?: { data?: { detail?: string; message?: string } } };
  return r.response?.data?.detail ?? r.response?.data?.message ?? fallback;
}

export default function StockAdjustments(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<StockAdjustment[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState('');
  const [adjustmentType, setAdjustmentType] = useState('INCREASE');
  const [warehouseId, setWarehouseId] = useState('');
  const [reasonCode, setReasonCode] = useState('');
  const [lines, setLines] = useState<StockAdjustmentLine[]>([{ ...emptyLine }]);

  async function load(): Promise<void> {
    setLoading(true); setError('');
    try { const res = await stockAdjustmentsApi.list(1, 100); setRows(unwrapList<StockAdjustment>(res.data)); }
    catch (e: unknown) { setError(extractError(e, 'تعذر تحميل تسويات المخزون')); }
    finally { setLoading(false); }
  }

  useEffect(() => { void load(); }, []);

  function updateLine(idx: number, patch: Partial<StockAdjustmentLine>): void {
    setLines((prev) => prev.map((l, i) => {
      if (i !== idx) return l;
      const merged = { ...l, ...patch };
      merged.systemQtyAfter = Number(merged.systemQtyBefore) + Number(merged.qtyDelta);
      return merged;
    }));
  }

  async function create(): Promise<void> {
    if (!warehouseId.trim() || !reasonCode.trim()) { setError('المستودع وكود السبب مطلوبان'); return; }
    const cleanLines = lines.filter((l) => l.itemId.trim() && l.locationId.trim() && Number(l.qtyDelta) !== 0).map((l) => ({
      itemId: l.itemId.trim(), locationId: l.locationId.trim(), status: l.status,
      qtyDelta: Number(l.qtyDelta), systemQtyBefore: Number(l.systemQtyBefore),
      systemQtyAfter: Number(l.systemQtyBefore) + Number(l.qtyDelta),
      notes: l.notes?.trim() || undefined,
    }));
    if (cleanLines.length === 0) { setError('أضف سطراً واحداً على الأقل بكمية تعديل غير صفرية'); return; }
    setBusy('create');
    try {
      await stockAdjustmentsApi.create({ adjustmentType, warehouseId: warehouseId.trim(), reasonCode: reasonCode.trim(), lines: cleanLines } as CreateStockAdjustment);
      setWarehouseId(''); setReasonCode(''); setLines([{ ...emptyLine }]); setShowForm(false); await load();
    } catch (e: unknown) { setError(extractError(e, 'تعذر إنشاء التسوية')); }
    finally { setBusy(''); }
  }

  async function post(id: string): Promise<void> {
    setBusy(id);
    try { await stockAdjustmentsApi.post(id); await load(); }
    catch (e: unknown) { setError(extractError(e, 'تعذر ترحيل التسوية')); }
    finally { setBusy(''); }
  }

  const ADJUSTMENT_TYPE_LABELS: Record<string, string> = { INCREASE: 'زيادة', DECREASE: 'نقص', RECOUNT: 'إعادة جرد' };

  if (loading) return <LoadingSpinner />;

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

      {error ? <ErrorBanner message={error} /> : null}

      {showForm ? (
        <div className="vex-card" style={{ marginBottom: 20 }}>
          <h2 className="vex-section-title">تسوية مخزون جديدة</h2>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 16, marginBottom: 20 }}>
            <label className="vex-label">
              نوع التسوية
              <select value={adjustmentType} onChange={(e) => setAdjustmentType(e.target.value)} className="vex-select">
                <option value="INCREASE">زيادة</option>
                <option value="DECREASE">نقص</option>
                <option value="RECOUNT">إعادة جرد</option>
              </select>
            </label>
            <label className="vex-label">
              المستودع *
              <input value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} className="vex-input" placeholder="Warehouse ID" />
            </label>
            <label className="vex-label">
              كود السبب *
              <input value={reasonCode} onChange={(e) => setReasonCode(e.target.value)} className="vex-input" placeholder="Reason Code" />
            </label>
          </div>

          <h3 className="vex-section-title" style={{ marginBottom: 12 }}>الأصناف</h3>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10, marginBottom: 14 }}>
            {lines.map((l, idx) => (
              <div key={idx} style={{ background: 'var(--clr-surface-2)', border: '1px solid var(--clr-border)', borderRadius: 'var(--radius-md)', padding: 14, position: 'relative' }}>
                <div style={{ position: 'absolute', top: 10, left: 10, width: 22, height: 22, background: 'var(--clr-primary-light)', color: 'var(--clr-primary)', borderRadius: '50%', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 11, fontWeight: 700 }}>{idx + 1}</div>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(140px, 1fr))', gap: 12, paddingLeft: 32 }}>
                  <label className="vex-label">الصنف <input value={l.itemId} onChange={(e) => updateLine(idx, { itemId: e.target.value })} className="vex-input" placeholder="Item ID" /></label>
                  <label className="vex-label">الموقع <input value={l.locationId} onChange={(e) => updateLine(idx, { locationId: e.target.value })} className="vex-input" placeholder="Location ID" /></label>
                  <label className="vex-label">الكمية الحالية <input type="number" value={l.systemQtyBefore} onChange={(e) => updateLine(idx, { systemQtyBefore: Number(e.target.value) })} className="vex-input" /></label>
                  <label className="vex-label">مقدار التغيير <input type="number" value={l.qtyDelta} onChange={(e) => updateLine(idx, { qtyDelta: Number(e.target.value) })} className="vex-input" /></label>
                  <label className="vex-label">
                    الناتج
                    <input type="number" value={l.systemQtyAfter} readOnly className="vex-input" style={{ background: 'var(--clr-surface-2)', color: l.systemQtyAfter >= 0 ? '#22c55e' : 'var(--clr-danger)', fontWeight: 700 }} />
                  </label>
                </div>
                <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 10 }}>
                  <button type="button" onClick={() => setLines((prev) => prev.length > 1 ? prev.filter((_, i) => i !== idx) : prev)} className="btn-danger" style={{ padding: '4px 12px', fontSize: 12 }}>✕ حذف</button>
                </div>
              </div>
            ))}
          </div>
          <div style={{ display: 'flex', gap: 10 }}>
            <button type="button" onClick={() => setLines((prev) => [...prev, { ...emptyLine }])} className="btn-secondary">＋ إضافة سطر</button>
            <button type="button" disabled={busy === 'create'} onClick={() => void create()} className="btn-primary">💾 حفظ التسوية</button>
          </div>
        </div>
      ) : null}

      <div className="vex-card vex-card--no-pad">
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead>
              <tr>
                <th>رقم التسوية</th>
                <th>النوع</th>
                <th>المستودع</th>
                <th>السبب</th>
                <th>الحالة</th>
                <th>إجراءات</th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 ? (
                <tr><td colSpan={6} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>لا توجد تسويات</td></tr>
              ) : rows.map((a) => (
                <tr key={a.id}>
                  <td style={{ fontWeight: 600, color: 'var(--clr-primary)' }}>{a.adjustmentNo}</td>
                  <td>
                    <span className={`badge ${a.adjustmentType === 'INCREASE' ? 'badge--success' : a.adjustmentType === 'DECREASE' ? 'badge--danger' : 'badge--warning'}`}>
                      {ADJUSTMENT_TYPE_LABELS[a.adjustmentType] ?? a.adjustmentType}
                    </span>
                  </td>
                  <td><span className="badge badge--draft">{a.warehouseId.slice(0, 8)}</span></td>
                  <td style={{ color: 'var(--txt-secondary)' }}>{a.reasonCode}</td>
                  <td><StatusBadge status={a.status} type="invoice" /></td>
                  <td>
                    {a.status !== 'POSTED' ? (
                      <button type="button" disabled={busy === a.id} onClick={() => void post(a.id)} className="btn-success" style={{ padding: '5px 14px', fontSize: 12 }}>✓ ترحيل</button>
                    ) : <span className="badge badge--success">مرحّل</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

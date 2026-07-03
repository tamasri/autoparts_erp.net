import { useEffect, useState } from 'react';
import {
  stockAdjustmentsApi,
  type CreateStockAdjustment,
  type StockAdjustmentLine,
} from '../../api/endpoints/stockAdjustments';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

type StockAdjustment = {
  id: string;
  adjustmentNo: string;
  adjustmentType: string;
  warehouseId: string;
  reasonCode: string;
  status: string;
  postedAt?: string;
};

function extractError(e: unknown, fallback: string): string {
  const r = e as { response?: { data?: { detail?: string; message?: string } } };
  return r.response?.data?.detail ?? r.response?.data?.message ?? fallback;
}

const emptyLine: StockAdjustmentLine = {
  itemId: '',
  locationId: '',
  status: 'AVAILABLE',
  qtyDelta: 0,
  systemQtyBefore: 0,
  systemQtyAfter: 0,
};

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
    setLoading(true);
    setError('');
    try {
      const res = await stockAdjustmentsApi.list(1, 100);
      setRows(unwrapList<StockAdjustment>(res.data));
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر تحميل تسويات المخزون'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  function updateLine(idx: number, patch: Partial<StockAdjustmentLine>): void {
    setLines((prev) => prev.map((l, i) => {
      if (i !== idx) return l;
      const merged = { ...l, ...patch };
      merged.systemQtyAfter = Number(merged.systemQtyBefore) + Number(merged.qtyDelta);
      return merged;
    }));
  }

  function addLine(): void {
    setLines((prev) => [...prev, { ...emptyLine }]);
  }

  function removeLine(idx: number): void {
    setLines((prev) => (prev.length > 1 ? prev.filter((_, i) => i !== idx) : prev));
  }

  async function create(): Promise<void> {
    if (!warehouseId.trim() || !reasonCode.trim()) {
      setError('المستودع وكود السبب مطلوبان');
      return;
    }
    const cleanLines = lines
      .filter((l) => l.itemId.trim() && l.locationId.trim() && Number(l.qtyDelta) !== 0)
      .map((l) => ({
        itemId: l.itemId.trim(),
        locationId: l.locationId.trim(),
        status: l.status,
        qtyDelta: Number(l.qtyDelta),
        systemQtyBefore: Number(l.systemQtyBefore),
        systemQtyAfter: Number(l.systemQtyBefore) + Number(l.qtyDelta),
        notes: l.notes?.trim() || undefined,
      }));
    if (cleanLines.length === 0) {
      setError('أضف سطراً واحداً على الأقل بكمية تعديل غير صفرية');
      return;
    }
    setBusy('create');
    try {
      const payload: CreateStockAdjustment = {
        adjustmentType,
        warehouseId: warehouseId.trim(),
        reasonCode: reasonCode.trim(),
        lines: cleanLines,
      };
      await stockAdjustmentsApi.create(payload);
      setWarehouseId('');
      setReasonCode('');
      setLines([{ ...emptyLine }]);
      setShowForm(false);
      await load();
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر إنشاء التسوية'));
    } finally {
      setBusy('');
    }
  }

  async function post(id: string): Promise<void> {
    setBusy(id);
    try {
      await stockAdjustmentsApi.post(id);
      await load();
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر ترحيل التسوية'));
    } finally {
      setBusy('');
    }
  }

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <h2 style={{ marginTop: 0 }}>تسويات المخزون</h2>
        <button type="button" onClick={() => setShowForm((s) => !s)} style={btn('#00796b')}>
          {showForm ? 'إلغاء' : '+ تسوية'}
        </button>
      </div>
      {error ? <ErrorBanner message={error} /> : null}

      {showForm ? (
        <div style={card()}>
          <div style={grid()}>
            <label style={lbl()}>نوع التسوية
              <select value={adjustmentType} onChange={(e) => setAdjustmentType(e.target.value)} style={inp()}>
                <option value="INCREASE">زيادة</option>
                <option value="DECREASE">نقص</option>
                <option value="RECOUNT">إعادة جرد</option>
              </select>
            </label>
            <label style={lbl()}>المستودع*
              <input value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} style={inp()} placeholder="Warehouse ID" />
            </label>
            <label style={lbl()}>كود السبب*
              <input value={reasonCode} onChange={(e) => setReasonCode(e.target.value)} style={inp()} placeholder="Reason Code" />
            </label>
          </div>

          <div style={{ fontSize: '13px', fontWeight: 700, margin: '8px 0', color: '#00695c' }}>الأصناف</div>
          {lines.map((l, idx) => (
            <div key={idx} style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(150px, 1fr)) 40px', gap: '8px', marginBottom: '8px', alignItems: 'end' }}>
              <label style={lbl()}>الصنف
                <input value={l.itemId} onChange={(e) => updateLine(idx, { itemId: e.target.value })} style={inp()} placeholder="Item ID" />
              </label>
              <label style={lbl()}>الموقع
                <input value={l.locationId} onChange={(e) => updateLine(idx, { locationId: e.target.value })} style={inp()} placeholder="Location ID" />
              </label>
              <label style={lbl()}>الكمية الحالية
                <input type="number" value={l.systemQtyBefore} onChange={(e) => updateLine(idx, { systemQtyBefore: Number(e.target.value) })} style={inp()} />
              </label>
              <label style={lbl()}>مقدار التغيير
                <input type="number" value={l.qtyDelta} onChange={(e) => updateLine(idx, { qtyDelta: Number(e.target.value) })} style={inp()} />
              </label>
              <label style={lbl()}>الناتج
                <input type="number" value={l.systemQtyAfter} readOnly style={{ ...inp(), background: '#f0f0f0' }} />
              </label>
              <button type="button" onClick={() => removeLine(idx)} style={btn('#c62828')}>×</button>
            </div>
          ))}
          <button type="button" onClick={addLine} style={btn('#455a64')}>+ سطر</button>
          <div style={{ marginTop: '12px' }}>
            <button type="button" disabled={busy === 'create'} onClick={() => void create()} style={btn('#004d40')}>حفظ التسوية</button>
          </div>
        </div>
      ) : null}

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto', marginTop: '12px' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th>رقم التسوية</th><th>النوع</th><th>المستودع</th><th>السبب</th><th>الحالة</th><th>إجراءات</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr><td colSpan={6} style={{ textAlign: 'center', padding: '16px', color: '#777' }}>لا توجد تسويات</td></tr>
            ) : rows.map((a) => (
              <tr key={a.id}>
                <td>{a.adjustmentNo}</td>
                <td>{a.adjustmentType}</td>
                <td>{a.warehouseId.slice(0, 8)}</td>
                <td>{a.reasonCode}</td>
                <td>{a.status}</td>
                <td style={{ whiteSpace: 'nowrap' }}>
                  {a.status !== 'POSTED' ? (
                    <button type="button" disabled={busy === a.id} onClick={() => void post(a.id)} style={btn('#00796b')}>ترحيل</button>
                  ) : <span style={{ color: '#2e7d32' }}>مرحّل</span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function btn(bg: string): React.CSSProperties {
  return { border: 'none', borderRadius: '8px', background: bg, color: '#fff', padding: '6px 12px', margin: '0 4px', cursor: 'pointer', fontSize: '13px' };
}
function card(): React.CSSProperties {
  return { background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', padding: '16px', marginTop: '12px' };
}
function grid(): React.CSSProperties {
  return { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: '12px', marginBottom: '12px' };
}
function lbl(): React.CSSProperties {
  return { display: 'flex', flexDirection: 'column', gap: '4px', fontSize: '13px', color: '#333' };
}
function inp(): React.CSSProperties {
  return { padding: '8px', border: '1px solid #b0bec5', borderRadius: '8px' };
}

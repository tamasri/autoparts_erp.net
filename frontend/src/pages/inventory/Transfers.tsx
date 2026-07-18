import { useEffect, useState } from 'react';
import {
  transfersApi,
  type CreateTransferOrder,
  type TransferOrderLine,
} from '../../api/endpoints/transfers';
import { unwrapList } from '../../api/apiData';
import { toast, extractApiError } from '../../lib/toast';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

type TransferOrder = {
  id: string;
  orderNo: string;
  sourceWarehouseId: string;
  destinationWarehouseId: string;
  status: string;
  shippedAt?: string;
  receivedAt?: string;
};

function extractError(e: unknown, fallback: string): string {
  return extractApiError(e, fallback);
}

const emptyLine: TransferOrderLine = {
  itemId: '',
  sourceLocationId: '',
  destinationLocationId: '',
  shippedQty: 0,
};

export default function Transfers(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<TransferOrder[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState('');
  const [sourceWarehouseId, setSourceWarehouseId] = useState('');
  const [destinationWarehouseId, setDestinationWarehouseId] = useState('');
  const [lines, setLines] = useState<TransferOrderLine[]>([{ ...emptyLine }]);

  async function load(): Promise<void> {
    setLoading(true);
    setError('');
    try {
      const res = await transfersApi.listOrders(1, 100);
      setRows(unwrapList<TransferOrder>(res.data));
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر تحميل أوامر التحويل'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  function updateLine(idx: number, patch: Partial<TransferOrderLine>): void {
    setLines((prev) => prev.map((l, i) => (i === idx ? { ...l, ...patch } : l)));
  }

  function addLine(): void {
    setLines((prev) => [...prev, { ...emptyLine }]);
  }

  function removeLine(idx: number): void {
    setLines((prev) => (prev.length > 1 ? prev.filter((_, i) => i !== idx) : prev));
  }

  async function create(): Promise<void> {
    if (!sourceWarehouseId.trim() || !destinationWarehouseId.trim()) {
      setError('مستودع المصدر والوجهة مطلوبان');
      return;
    }
    const cleanLines = lines
      .filter((l) => l.itemId.trim() && l.shippedQty > 0)
      .map((l) => ({
        itemId: l.itemId.trim(),
        sourceLocationId: l.sourceLocationId?.trim() || undefined,
        destinationLocationId: l.destinationLocationId?.trim() || undefined,
        shippedQty: Number(l.shippedQty),
      }));
    if (cleanLines.length === 0) {
      setError('أضف سطراً واحداً على الأقل بكمية صحيحة');
      return;
    }
    setBusy('create');
    try {
      const payload: CreateTransferOrder = {
        sourceWarehouseId: sourceWarehouseId.trim(),
        destinationWarehouseId: destinationWarehouseId.trim(),
        lines: cleanLines,
      };
      await transfersApi.createOrder(payload);
      setSourceWarehouseId('');
      setDestinationWarehouseId('');
      setLines([{ ...emptyLine }]);
      setShowForm(false);
      toast.success('تم إنشاء أمر التحويل');
      await load();
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر إنشاء أمر التحويل'));
      setError(extractApiError(e, 'تعذر إنشاء أمر التحويل'));
    } finally {
      setBusy('');
    }
  }

  async function ship(id: string): Promise<void> {
    setBusy(id);
    try {
      await transfersApi.ship(id);
      toast.success('تم شحن أمر التحويل');
      await load();
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر شحن أمر التحويل'));
      setError(extractApiError(e, 'تعذر شحن أمر التحويل'));
    } finally {
      setBusy('');
    }
  }

  async function receive(id: string): Promise<void> {
    setBusy(id);
    try {
      await transfersApi.receive(id);
      toast.success('تم استلام أمر التحويل');
      await load();
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر استلام أمر التحويل'));
      setError(extractApiError(e, 'تعذر استلام أمر التحويل'));
    } finally {
      setBusy('');
    }
  }

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <h2 style={{ marginTop: 0 }}>التحويلات بين المستودعات</h2>
        <button type="button" onClick={() => setShowForm((s) => !s)} style={btn('#00796b')}>
          {showForm ? 'إلغاء' : '+ أمر تحويل'}
        </button>
      </div>
      {error ? <ErrorBanner message={error} /> : null}

      {showForm ? (
        <div style={card()}>
          <div style={grid()}>
            <label style={lbl()}>مستودع المصدر*
              <input value={sourceWarehouseId} onChange={(e) => setSourceWarehouseId(e.target.value)} style={inp()} placeholder="Source Warehouse ID" />
            </label>
            <label style={lbl()}>مستودع الوجهة*
              <input value={destinationWarehouseId} onChange={(e) => setDestinationWarehouseId(e.target.value)} style={inp()} placeholder="Destination Warehouse ID" />
            </label>
          </div>

          <div style={{ fontSize: '13px', fontWeight: 700, margin: '8px 0', color: '#00695c' }}>الأصناف</div>
          {lines.map((l, idx) => (
            <div key={idx} style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(160px, 1fr)) 40px', gap: '8px', marginBottom: '8px', alignItems: 'end' }}>
              <label style={lbl()}>الصنف
                <input value={l.itemId} onChange={(e) => updateLine(idx, { itemId: e.target.value })} style={inp()} placeholder="Item ID" />
              </label>
              <label style={lbl()}>موقع المصدر
                <input value={l.sourceLocationId} onChange={(e) => updateLine(idx, { sourceLocationId: e.target.value })} style={inp()} placeholder="From Location" />
              </label>
              <label style={lbl()}>موقع الوجهة
                <input value={l.destinationLocationId} onChange={(e) => updateLine(idx, { destinationLocationId: e.target.value })} style={inp()} placeholder="To Location" />
              </label>
              <label style={lbl()}>الكمية
                <input type="number" value={l.shippedQty} onChange={(e) => updateLine(idx, { shippedQty: Number(e.target.value) })} style={inp()} />
              </label>
              <button type="button" onClick={() => removeLine(idx)} style={btn('#c62828')}>×</button>
            </div>
          ))}
          <button type="button" onClick={addLine} style={btn('#455a64')}>+ سطر</button>
          <div style={{ marginTop: '12px' }}>
            <button type="button" disabled={busy === 'create'} onClick={() => void create()} style={btn('#004d40')}>حفظ الأمر</button>
          </div>
        </div>
      ) : null}

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto', marginTop: '12px' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th>رقم الأمر</th><th>المصدر</th><th>الوجهة</th><th>الحالة</th><th>إجراءات</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr><td colSpan={5} style={{ textAlign: 'center', padding: '16px', color: '#777' }}>لا توجد أوامر تحويل</td></tr>
            ) : rows.map((o) => (
              <tr key={o.id}>
                <td>{o.orderNo}</td>
                <td>{o.sourceWarehouseId.slice(0, 8)}</td>
                <td>{o.destinationWarehouseId.slice(0, 8)}</td>
                <td>{o.status}</td>
                <td style={{ whiteSpace: 'nowrap' }}>
                  {o.status !== 'SHIPPED' && o.status !== 'RECEIVED' ? (
                    <button type="button" disabled={busy === o.id} onClick={() => void ship(o.id)} style={btn('#00796b')}>شحن</button>
                  ) : null}
                  {o.status === 'SHIPPED' ? (
                    <button type="button" disabled={busy === o.id} onClick={() => void receive(o.id)} style={btn('#004d40')}>استلام</button>
                  ) : null}
                  {o.status === 'RECEIVED' ? <span style={{ color: '#2e7d32' }}>مكتمل</span> : null}
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

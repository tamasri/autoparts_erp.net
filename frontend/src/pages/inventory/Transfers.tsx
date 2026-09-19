import { useEffect, useState } from 'react';
import { transfersApi, type CreateTransferOrder, type TransferOrderLine } from '../../api/endpoints/transfers';
import { unwrapList } from '../../api/apiData';
import { toast, extractApiError } from '../../lib/toast';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';
import StatusBadge from '../../components/common/StatusBadge';

type TransferOrder = {
  id: string;
  orderNo: string;
  sourceWarehouseId: string;
  destinationWarehouseId: string;
  status: string;
  shippedAt?: string;
  receivedAt?: string;
};

const emptyLine: TransferOrderLine = { itemId: '', sourceLocationId: '', destinationLocationId: '', shippedQty: 0 };

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
    setLoading(true); setError('');
    try {
      const res = await transfersApi.listOrders(1, 100);
      setRows(unwrapList<TransferOrder>(res.data));
    } catch (e: unknown) { setError(extractApiError(e, 'تعذر تحميل أوامر التحويل')); }
    finally { setLoading(false); }
  }

  useEffect(() => { void load(); }, []);

  function updateLine(idx: number, patch: Partial<TransferOrderLine>): void {
    setLines((prev) => prev.map((l, i) => (i === idx ? { ...l, ...patch } : l)));
  }

  async function create(): Promise<void> {
    if (!sourceWarehouseId.trim() || !destinationWarehouseId.trim()) { setError('مستودع المصدر والوجهة مطلوبان'); return; }
    const cleanLines = lines.filter((l) => l.itemId.trim() && l.shippedQty > 0).map((l) => ({
      itemId: l.itemId.trim(),
      sourceLocationId: l.sourceLocationId?.trim() || undefined,
      destinationLocationId: l.destinationLocationId?.trim() || undefined,
      shippedQty: Number(l.shippedQty),
    }));
    if (cleanLines.length === 0) { setError('أضف سطراً واحداً على الأقل بكمية صحيحة'); return; }
    setBusy('create');
    try {
      await transfersApi.createOrder({ sourceWarehouseId: sourceWarehouseId.trim(), destinationWarehouseId: destinationWarehouseId.trim(), lines: cleanLines } as CreateTransferOrder);
      setSourceWarehouseId(''); setDestinationWarehouseId(''); setLines([{ ...emptyLine }]); setShowForm(false);
      toast.success('تم إنشاء أمر التحويل'); await load();
    } catch (e: unknown) { toast.error(extractApiError(e, 'تعذر إنشاء أمر التحويل')); setError(extractApiError(e, 'تعذر إنشاء أمر التحويل')); }
    finally { setBusy(''); }
  }

  async function ship(id: string): Promise<void> {
    setBusy(id);
    try { await transfersApi.ship(id); toast.success('تم شحن أمر التحويل'); await load(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر شحن أمر التحويل')); }
    finally { setBusy(''); }
  }

  async function receive(id: string): Promise<void> {
    setBusy(id);
    try { await transfersApi.receive(id); toast.success('تم استلام أمر التحويل'); await load(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر استلام أمر التحويل')); }
    finally { setBusy(''); }
  }

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">التحويلات بين المستودعات</h1>
          <div className="vex-page-header__breadcrumb">نقل المخزون بين المستودعات والمواقع</div>
        </div>
        <button type="button" onClick={() => setShowForm((s) => !s)} className={showForm ? 'btn-ghost' : 'btn-primary'}>
          {showForm ? '✕ إلغاء' : '＋ أمر تحويل'}
        </button>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {showForm ? (
        <div className="vex-card" style={{ marginBottom: 20 }}>
          <h2 className="vex-section-title">أمر تحويل جديد</h2>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 16, marginBottom: 20 }}>
            <label className="vex-label">
              مستودع المصدر *
              <input value={sourceWarehouseId} onChange={(e) => setSourceWarehouseId(e.target.value)} className="vex-input" placeholder="Source Warehouse ID" />
            </label>
            <label className="vex-label">
              مستودع الوجهة *
              <input value={destinationWarehouseId} onChange={(e) => setDestinationWarehouseId(e.target.value)} className="vex-input" placeholder="Destination Warehouse ID" />
            </label>
          </div>

          <h3 className="vex-section-title" style={{ marginBottom: 12 }}>الأصناف</h3>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10, marginBottom: 14 }}>
            {lines.map((l, idx) => (
              <div key={idx} style={{
                background: 'var(--clr-surface-2)', border: '1px solid var(--clr-border)',
                borderRadius: 'var(--radius-md)', padding: 14, position: 'relative',
              }}>
                <div style={{ position: 'absolute', top: 10, left: 10, width: 22, height: 22, background: 'var(--clr-primary-light)', color: 'var(--clr-primary)', borderRadius: '50%', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 11, fontWeight: 700 }}>{idx + 1}</div>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(150px, 1fr))', gap: 12, paddingLeft: 32 }}>
                  <label className="vex-label">الصنف <input value={l.itemId} onChange={(e) => updateLine(idx, { itemId: e.target.value })} className="vex-input" placeholder="Item ID" /></label>
                  <label className="vex-label">موقع المصدر <input value={l.sourceLocationId} onChange={(e) => updateLine(idx, { sourceLocationId: e.target.value })} className="vex-input" /></label>
                  <label className="vex-label">موقع الوجهة <input value={l.destinationLocationId} onChange={(e) => updateLine(idx, { destinationLocationId: e.target.value })} className="vex-input" /></label>
                  <label className="vex-label">الكمية <input type="number" value={l.shippedQty} onChange={(e) => updateLine(idx, { shippedQty: Number(e.target.value) })} className="vex-input" /></label>
                </div>
                <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 10, paddingLeft: 32 }}>
                  <button type="button" onClick={() => setLines((prev) => prev.length > 1 ? prev.filter((_, i) => i !== idx) : prev)} className="btn-danger" style={{ padding: '4px 12px', fontSize: 12 }}>✕ حذف</button>
                </div>
              </div>
            ))}
          </div>
          <div style={{ display: 'flex', gap: 10 }}>
            <button type="button" onClick={() => setLines((prev) => [...prev, { ...emptyLine }])} className="btn-secondary">＋ إضافة سطر</button>
            <button type="button" disabled={busy === 'create'} onClick={() => void create()} className="btn-primary">💾 حفظ الأمر</button>
          </div>
        </div>
      ) : null}

      <div className="vex-card vex-card--no-pad">
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead>
              <tr>
                <th>رقم الأمر</th>
                <th>المصدر</th>
                <th>الوجهة</th>
                <th>الحالة</th>
                <th>إجراءات</th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 ? (
                <tr><td colSpan={5} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>لا توجد أوامر تحويل</td></tr>
              ) : rows.map((o) => (
                <tr key={o.id}>
                  <td style={{ fontWeight: 600, color: 'var(--clr-primary)' }}>{o.orderNo}</td>
                  <td><span className="badge badge--draft">{o.sourceWarehouseId.slice(0, 8)}</span></td>
                  <td><span className="badge badge--primary">{o.destinationWarehouseId.slice(0, 8)}</span></td>
                  <td><StatusBadge status={o.status} type="invoice" /></td>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    {o.status !== 'SHIPPED' && o.status !== 'RECEIVED' ? (
                      <button type="button" disabled={busy === o.id} onClick={() => void ship(o.id)} className="btn-primary" style={{ padding: '5px 14px', fontSize: 12, marginLeft: 6 }}>
                        ✈ شحن
                      </button>
                    ) : null}
                    {o.status === 'SHIPPED' ? (
                      <button type="button" disabled={busy === o.id} onClick={() => void receive(o.id)} className="btn-success" style={{ padding: '5px 14px', fontSize: 12 }}>
                        ✓ استلام
                      </button>
                    ) : null}
                    {o.status === 'RECEIVED' ? <span className="badge badge--success">مكتمل</span> : null}
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

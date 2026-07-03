import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { invoicesApi, type CreateInvoice, type CreateInvoiceLine } from '../../api/endpoints/invoices';
import { customersApi } from '../../api/endpoints/customers';
import { unwrapList, unwrapNode } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

type CustomerOption = { id: string; code?: string; name?: string };

function extractError(e: unknown, fallback: string): string {
  const r = e as { response?: { data?: { detail?: string; message?: string } } };
  return r.response?.data?.detail ?? r.response?.data?.message ?? fallback;
}

const today = new Date().toISOString().slice(0, 10);

const emptyLine: CreateInvoiceLine = {
  skuId: '',
  locationId: '',
  quantity: 1,
  unitPriceSyp: 0,
  unitPriceUsd: 0,
  discountPct: 0,
  isPriceOverride: false,
};

export default function InvoiceWorkspace(): JSX.Element {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [customers, setCustomers] = useState<CustomerOption[]>([]);

  const [customerId, setCustomerId] = useState('');
  const [invoiceDate, setInvoiceDate] = useState(today);
  const [dueDate, setDueDate] = useState(today);
  const [fxRateId, setFxRateId] = useState('');
  const [invoiceType, setInvoiceType] = useState('SALE');
  const [salesRepId, setSalesRepId] = useState('');
  const [deliveryFeeSyp, setDeliveryFeeSyp] = useState(0);
  const [deliveryFeeUsd, setDeliveryFeeUsd] = useState(0);
  const [lines, setLines] = useState<CreateInvoiceLine[]>([{ ...emptyLine }]);

  useEffect(() => {
    let mounted = true;
    async function load(): Promise<void> {
      setLoading(true);
      try {
        const res = await customersApi.getCustomers({ page: 1, pageSize: 200, isActive: true });
        if (mounted) setCustomers(unwrapList<CustomerOption>(res.data));
      } catch (e: unknown) {
        if (mounted) setError(extractError(e, 'تعذر تحميل العملاء'));
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void load();
    return () => { mounted = false; };
  }, []);

  function updateLine(idx: number, patch: Partial<CreateInvoiceLine>): void {
    setLines((prev) => prev.map((l, i) => (i === idx ? { ...l, ...patch } : l)));
  }
  function addLine(): void {
    setLines((prev) => [...prev, { ...emptyLine }]);
  }
  function removeLine(idx: number): void {
    setLines((prev) => (prev.length > 1 ? prev.filter((_, i) => i !== idx) : prev));
  }

  const grandTotal = lines.reduce((sum, l) => {
    const gross = Number(l.quantity) * Number(l.unitPriceSyp);
    return sum + gross * (1 - Number(l.discountPct) / 100);
  }, Number(deliveryFeeSyp));

  async function submit(): Promise<void> {
    if (!customerId) { setError('اختر العميل'); return; }
    if (!fxRateId.trim()) { setError('معرّف سعر الصرف (FX Rate ID) مطلوب'); return; }
    const cleanLines = lines
      .filter((l) => l.skuId.trim() && l.locationId.trim() && Number(l.quantity) > 0)
      .map((l) => ({
        skuId: l.skuId.trim(),
        batchId: l.batchId?.trim() || undefined,
        locationId: l.locationId.trim(),
        quantity: Number(l.quantity),
        unitPriceSyp: Number(l.unitPriceSyp),
        unitPriceUsd: Number(l.unitPriceUsd),
        discountPct: Number(l.discountPct),
        isPriceOverride: Boolean(l.isPriceOverride),
        overrideReason: l.isPriceOverride ? (l.overrideReason?.trim() || 'Manual override') : undefined,
      }));
    if (cleanLines.length === 0) { setError('أضف سطراً صحيحاً واحداً على الأقل'); return; }

    setBusy(true);
    setError('');
    try {
      const payload: CreateInvoice = {
        customerId,
        invoiceDate,
        dueDate,
        fxRateId: fxRateId.trim(),
        invoiceType,
        salesRepId: salesRepId.trim() || undefined,
        deliveryFeeSyp: Number(deliveryFeeSyp),
        deliveryFeeUsd: Number(deliveryFeeUsd),
        lines: cleanLines,
      };
      const res = await invoicesApi.createInvoice(payload);
      const created = unwrapNode<{ id?: string }>(res.data);
      if (created?.id) navigate(`/invoices/${created.id}`);
      else navigate('/invoices');
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر إنشاء الفاتورة'));
    } finally {
      setBusy(false);
    }
  }

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <h2 style={{ marginTop: 0 }}>فاتورة جديدة</h2>
        <button type="button" onClick={() => navigate('/invoices')} style={btn('#607d8b')}>رجوع</button>
      </div>
      {error ? <ErrorBanner message={error} /> : null}

      <div style={card()}>
        <div style={grid()}>
          <label style={lbl()}>العميل*
            <select value={customerId} onChange={(e) => setCustomerId(e.target.value)} style={inp()}>
              <option value="">— اختر —</option>
              {customers.map((c) => (
                <option key={c.id} value={c.id}>{c.code ? `${c.code} - ` : ''}{c.name ?? c.id.slice(0, 8)}</option>
              ))}
            </select>
          </label>
          <label style={lbl()}>نوع الفاتورة
            <select value={invoiceType} onChange={(e) => setInvoiceType(e.target.value)} style={inp()}>
              <option value="SALE">بيع</option>
              <option value="RETURN">مرتجع</option>
            </select>
          </label>
          <label style={lbl()}>تاريخ الفاتورة
            <input type="date" value={invoiceDate} onChange={(e) => setInvoiceDate(e.target.value)} style={inp()} />
          </label>
          <label style={lbl()}>تاريخ الاستحقاق
            <input type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} style={inp()} />
          </label>
          <label style={lbl()}>سعر الصرف (FX Rate ID)*
            <input value={fxRateId} onChange={(e) => setFxRateId(e.target.value)} style={inp()} placeholder="FX Rate ID" />
          </label>
          <label style={lbl()}>مندوب المبيعات
            <input value={salesRepId} onChange={(e) => setSalesRepId(e.target.value)} style={inp()} placeholder="Sales Rep ID (optional)" />
          </label>
          <label style={lbl()}>رسوم التوصيل ل.س
            <input type="number" value={deliveryFeeSyp} onChange={(e) => setDeliveryFeeSyp(Number(e.target.value))} style={inp()} />
          </label>
          <label style={lbl()}>رسوم التوصيل $
            <input type="number" value={deliveryFeeUsd} onChange={(e) => setDeliveryFeeUsd(Number(e.target.value))} style={inp()} />
          </label>
        </div>

        <div style={{ fontSize: '13px', fontWeight: 700, margin: '8px 0', color: '#00695c' }}>الأسطر</div>
        {lines.map((l, idx) => (
          <div key={idx} style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(130px, 1fr)) 40px', gap: '8px', marginBottom: '8px', alignItems: 'end' }}>
            <label style={lbl()}>SKU
              <input value={l.skuId} onChange={(e) => updateLine(idx, { skuId: e.target.value })} style={inp()} placeholder="SKU ID" />
            </label>
            <label style={lbl()}>الموقع
              <input value={l.locationId} onChange={(e) => updateLine(idx, { locationId: e.target.value })} style={inp()} placeholder="Location ID" />
            </label>
            <label style={lbl()}>الدفعة
              <input value={l.batchId ?? ''} onChange={(e) => updateLine(idx, { batchId: e.target.value })} style={inp()} placeholder="Batch (optional)" />
            </label>
            <label style={lbl()}>الكمية
              <input type="number" value={l.quantity} onChange={(e) => updateLine(idx, { quantity: Number(e.target.value) })} style={inp()} />
            </label>
            <label style={lbl()}>سعر ل.س
              <input type="number" value={l.unitPriceSyp} onChange={(e) => updateLine(idx, { unitPriceSyp: Number(e.target.value) })} style={inp()} />
            </label>
            <label style={lbl()}>سعر $
              <input type="number" value={l.unitPriceUsd} onChange={(e) => updateLine(idx, { unitPriceUsd: Number(e.target.value) })} style={inp()} />
            </label>
            <label style={lbl()}>الخصم %
              <input type="number" value={l.discountPct} onChange={(e) => updateLine(idx, { discountPct: Number(e.target.value) })} style={inp()} />
            </label>
            <button type="button" onClick={() => removeLine(idx)} style={btn('#c62828')}>×</button>
          </div>
        ))}
        <button type="button" onClick={addLine} style={btn('#455a64')}>+ سطر</button>

        <div style={{ marginTop: '12px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <div style={{ fontWeight: 800, color: '#00695c' }}>الإجمالي التقديري: {grandTotal.toLocaleString('en-US')} ل.س</div>
          <button type="button" disabled={busy} onClick={() => void submit()} style={btn('#004d40')}>حفظ الفاتورة (مسودة)</button>
        </div>
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
  return { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: '12px', marginBottom: '12px' };
}
function lbl(): React.CSSProperties {
  return { display: 'flex', flexDirection: 'column', gap: '4px', fontSize: '13px', color: '#333' };
}
function inp(): React.CSSProperties {
  return { padding: '8px', border: '1px solid #b0bec5', borderRadius: '8px' };
}

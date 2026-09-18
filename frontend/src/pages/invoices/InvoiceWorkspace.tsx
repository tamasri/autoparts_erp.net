import { useEffect, useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
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

// ── Wizard step definitions ──
const STEPS = [
  { label: 'بيانات الفاتورة', sublabel: 'المعلومات الأساسية' },
  { label: 'الأسطر', sublabel: 'الأصناف والكميات' },
  { label: 'المراجعة', sublabel: 'التحقق والحفظ' },
];

function StepCircle({ index, currentStep }: { index: number; currentStep: number }) {
  const state = index < currentStep ? 'completed' : index === currentStep ? 'active' : 'inactive';
  return (
    <div className={`vex-stepper__item vex-stepper__item--${state}`}>
      <div className="vex-stepper__circle">
        {state === 'completed' ? '✓' : index + 1}
      </div>
      <div className="vex-stepper__label">
        <div style={{ fontWeight: 600 }}>{STEPS[index].label}</div>
        <div style={{ fontSize: 11, marginTop: 2, opacity: 0.7 }}>{STEPS[index].sublabel}</div>
      </div>
    </div>
  );
}

export default function InvoiceWorkspace(): JSX.Element {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [customers, setCustomers] = useState<CustomerOption[]>([]);
  const [currentStep, setCurrentStep] = useState(0);

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

  function validateStep0(): boolean {
    if (!customerId) { setError('اختر العميل'); return false; }
    if (!fxRateId.trim()) { setError('معرّف سعر الصرف (FX Rate ID) مطلوب'); return false; }
    setError('');
    return true;
  }
  function validateStep1(): boolean {
    const ok = lines.some((l) => l.skuId.trim() && l.locationId.trim() && Number(l.quantity) > 0);
    if (!ok) { setError('أضف سطراً صحيحاً واحداً على الأقل'); return false; }
    setError('');
    return true;
  }

  function goNext(): void {
    if (currentStep === 0 && !validateStep0()) return;
    if (currentStep === 1 && !validateStep1()) return;
    setCurrentStep((s) => Math.min(s + 1, STEPS.length - 1));
  }
  function goPrev(): void {
    setError('');
    setCurrentStep((s) => Math.max(s - 1, 0));
  }

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

  const selectedCustomer = customers.find((c) => c.id === customerId);

  return (
    <div style={{ direction: 'rtl' }}>

      {/* Page Header */}
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">فاتورة جديدة</h1>
          <div className="vex-page-header__breadcrumb">
            <Link to="/invoices" style={{ color: 'var(--clr-primary)', textDecoration: 'none' }}>الفواتير</Link>
            {' / '} إنشاء فاتورة جديدة
          </div>
        </div>
        <button type="button" onClick={() => navigate('/invoices')} className="btn-ghost">
          ← رجوع
        </button>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {/* Main Card */}
      <div className="vex-card">

        {/* ── Stepper ── */}
        <div className="vex-stepper">
          {STEPS.map((_, i) => (
            <StepCircle key={i} index={i} currentStep={currentStep} />
          ))}
        </div>

        <hr className="vex-divider" />

        {/* ── Step 0: Invoice Header Info ── */}
        {currentStep === 0 && (
          <div>
            <h2 className="vex-section-title" style={{ marginBottom: 24 }}>المعلومات الأساسية</h2>
            <div style={{
              display: 'grid',
              gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))',
              gap: 20,
              marginBottom: 8,
            }}>
              <label className="vex-label">
                العميل *
                <select
                  id="invoice-customer"
                  value={customerId}
                  onChange={(e) => setCustomerId(e.target.value)}
                  className="vex-select"
                >
                  <option value="">— اختر العميل —</option>
                  {customers.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.code ? `${c.code} - ` : ''}{c.name ?? c.id.slice(0, 8)}
                    </option>
                  ))}
                </select>
              </label>

              <label className="vex-label">
                نوع الفاتورة
                <select
                  id="invoice-type"
                  value={invoiceType}
                  onChange={(e) => setInvoiceType(e.target.value)}
                  className="vex-select"
                >
                  <option value="SALE">بيع</option>
                  <option value="RETURN">مرتجع</option>
                </select>
              </label>

              <label className="vex-label">
                تاريخ الفاتورة
                <input
                  id="invoice-date"
                  type="date"
                  value={invoiceDate}
                  onChange={(e) => setInvoiceDate(e.target.value)}
                  className="vex-input"
                />
              </label>

              <label className="vex-label">
                تاريخ الاستحقاق
                <input
                  id="invoice-due-date"
                  type="date"
                  value={dueDate}
                  onChange={(e) => setDueDate(e.target.value)}
                  className="vex-input"
                />
              </label>

              <label className="vex-label">
                سعر الصرف (FX Rate ID) *
                <input
                  id="invoice-fx-rate"
                  value={fxRateId}
                  onChange={(e) => setFxRateId(e.target.value)}
                  className="vex-input"
                  placeholder="FX Rate ID"
                />
              </label>

              <label className="vex-label">
                مندوب المبيعات
                <input
                  id="invoice-sales-rep"
                  value={salesRepId}
                  onChange={(e) => setSalesRepId(e.target.value)}
                  className="vex-input"
                  placeholder="Sales Rep ID (اختياري)"
                />
              </label>

              <label className="vex-label">
                رسوم التوصيل ل.س
                <input
                  id="invoice-delivery-syp"
                  type="number"
                  value={deliveryFeeSyp}
                  onChange={(e) => setDeliveryFeeSyp(Number(e.target.value))}
                  className="vex-input"
                />
              </label>

              <label className="vex-label">
                رسوم التوصيل $
                <input
                  id="invoice-delivery-usd"
                  type="number"
                  value={deliveryFeeUsd}
                  onChange={(e) => setDeliveryFeeUsd(Number(e.target.value))}
                  className="vex-input"
                />
              </label>
            </div>
          </div>
        )}

        {/* ── Step 1: Line Items ── */}
        {currentStep === 1 && (
          <div>
            <h2 className="vex-section-title" style={{ marginBottom: 24 }}>الأصناف والكميات</h2>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              {lines.map((l, idx) => (
                <div
                  key={idx}
                  style={{
                    background: 'var(--clr-surface-2)',
                    border: '1px solid var(--clr-border)',
                    borderRadius: 'var(--radius-md)',
                    padding: '16px',
                    position: 'relative',
                  }}
                >
                  <div style={{
                    position: 'absolute',
                    top: 12,
                    left: 12,
                    width: 24,
                    height: 24,
                    background: 'var(--clr-primary-light)',
                    color: 'var(--clr-primary)',
                    borderRadius: '50%',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    fontSize: 12,
                    fontWeight: 700,
                  }}>
                    {idx + 1}
                  </div>

                  <div style={{
                    display: 'grid',
                    gridTemplateColumns: 'repeat(auto-fill, minmax(140px, 1fr))',
                    gap: 12,
                    paddingLeft: 36,
                  }}>
                    <label className="vex-label">
                      SKU *
                      <input
                        value={l.skuId}
                        onChange={(e) => updateLine(idx, { skuId: e.target.value })}
                        className="vex-input"
                        placeholder="SKU ID"
                      />
                    </label>
                    <label className="vex-label">
                      الموقع *
                      <input
                        value={l.locationId}
                        onChange={(e) => updateLine(idx, { locationId: e.target.value })}
                        className="vex-input"
                        placeholder="Location ID"
                      />
                    </label>
                    <label className="vex-label">
                      الدفعة
                      <input
                        value={l.batchId ?? ''}
                        onChange={(e) => updateLine(idx, { batchId: e.target.value })}
                        className="vex-input"
                        placeholder="Batch (اختياري)"
                      />
                    </label>
                    <label className="vex-label">
                      الكمية *
                      <input
                        type="number"
                        value={l.quantity}
                        onChange={(e) => updateLine(idx, { quantity: Number(e.target.value) })}
                        className="vex-input"
                        min={1}
                      />
                    </label>
                    <label className="vex-label">
                      سعر الوحدة ل.س
                      <input
                        type="number"
                        value={l.unitPriceSyp}
                        onChange={(e) => updateLine(idx, { unitPriceSyp: Number(e.target.value) })}
                        className="vex-input"
                      />
                    </label>
                    <label className="vex-label">
                      سعر الوحدة $
                      <input
                        type="number"
                        value={l.unitPriceUsd}
                        onChange={(e) => updateLine(idx, { unitPriceUsd: Number(e.target.value) })}
                        className="vex-input"
                      />
                    </label>
                    <label className="vex-label">
                      الخصم %
                      <input
                        type="number"
                        value={l.discountPct}
                        onChange={(e) => updateLine(idx, { discountPct: Number(e.target.value) })}
                        className="vex-input"
                        min={0}
                        max={100}
                      />
                    </label>
                  </div>

                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 12, paddingLeft: 36 }}>
                    <div style={{ fontSize: 13, color: 'var(--txt-muted)' }}>
                      الإجمالي السطر:{' '}
                      <strong style={{ color: 'var(--txt-primary)' }}>
                        {(Number(l.quantity) * Number(l.unitPriceSyp) * (1 - Number(l.discountPct) / 100)).toLocaleString('en-US')} ل.س
                      </strong>
                    </div>
                    <button
                      type="button"
                      onClick={() => removeLine(idx)}
                      className="btn-danger"
                      style={{ padding: '6px 14px', fontSize: 13 }}
                    >
                      ✕ حذف
                    </button>
                  </div>
                </div>
              ))}
            </div>

            <button
              type="button"
              onClick={addLine}
              className="btn-secondary"
              style={{ marginTop: 14 }}
            >
              ＋ إضافة سطر
            </button>

            <div style={{
              marginTop: 20,
              padding: '16px 20px',
              background: 'var(--clr-primary-light)',
              borderRadius: 'var(--radius-md)',
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
            }}>
              <span style={{ fontSize: 14, fontWeight: 600, color: 'var(--clr-primary-dark)' }}>
                الإجمالي التقديري
              </span>
              <span style={{ fontSize: 22, fontWeight: 800, color: 'var(--clr-primary)' }}>
                {grandTotal.toLocaleString('en-US')} <span style={{ fontSize: 14, fontWeight: 500 }}>ل.س</span>
              </span>
            </div>
          </div>
        )}

        {/* ── Step 2: Review ── */}
        {currentStep === 2 && (
          <div>
            <h2 className="vex-section-title" style={{ marginBottom: 24 }}>مراجعة الفاتورة</h2>

            {/* Summary cards */}
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 16, marginBottom: 24 }}>
              <div style={{ background: 'var(--clr-surface-2)', border: '1px solid var(--clr-border)', borderRadius: 'var(--radius-md)', padding: '16px 20px' }}>
                <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--txt-muted)', textTransform: 'uppercase', marginBottom: 10 }}>بيانات الفاتورة</div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 8, fontSize: 13 }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <span style={{ color: 'var(--txt-muted)' }}>العميل</span>
                    <span style={{ fontWeight: 600 }}>{selectedCustomer?.name ?? customerId.slice(0, 8)}</span>
                  </div>
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <span style={{ color: 'var(--txt-muted)' }}>النوع</span>
                    <span style={{ fontWeight: 600 }}>{invoiceType === 'SALE' ? 'بيع' : 'مرتجع'}</span>
                  </div>
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <span style={{ color: 'var(--txt-muted)' }}>التاريخ</span>
                    <span style={{ fontWeight: 600 }}>{invoiceDate}</span>
                  </div>
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <span style={{ color: 'var(--txt-muted)' }}>الاستحقاق</span>
                    <span style={{ fontWeight: 600 }}>{dueDate}</span>
                  </div>
                </div>
              </div>

              <div style={{ background: 'var(--clr-primary-light)', border: '1px solid #c7c4ff', borderRadius: 'var(--radius-md)', padding: '16px 20px' }}>
                <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--clr-primary-dark)', textTransform: 'uppercase', marginBottom: 10 }}>الملخص المالي</div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 8, fontSize: 13 }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <span style={{ color: 'var(--clr-primary-dark)', opacity: 0.75 }}>عدد الأسطر</span>
                    <span style={{ fontWeight: 600, color: 'var(--clr-primary-dark)' }}>{lines.filter((l) => l.skuId.trim()).length}</span>
                  </div>
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <span style={{ color: 'var(--clr-primary-dark)', opacity: 0.75 }}>رسوم التوصيل</span>
                    <span style={{ fontWeight: 600, color: 'var(--clr-primary-dark)' }}>{Number(deliveryFeeSyp).toLocaleString('en-US')} ل.س</span>
                  </div>
                  <div style={{ display: 'flex', justifyContent: 'space-between', borderTop: '1px dashed #a5a0ff', paddingTop: 8, marginTop: 4 }}>
                    <span style={{ fontWeight: 700, color: 'var(--clr-primary-dark)' }}>الإجمالي التقديري</span>
                    <span style={{ fontWeight: 800, color: 'var(--clr-primary)', fontSize: 16 }}>{grandTotal.toLocaleString('en-US')} ل.س</span>
                  </div>
                </div>
              </div>
            </div>

            <div style={{ background: 'var(--clr-warning-light)', border: '1px solid #fde68a', borderRadius: 'var(--radius-md)', padding: '12px 16px', fontSize: 13, color: '#92400e' }}>
              <strong>ملاحظة:</strong> ستُحفظ الفاتورة كمسودة. يمكن تأكيدها وترحيلها لاحقاً من صفحة التفاصيل.
            </div>
          </div>
        )}

        <hr className="vex-divider" />

        {/* Navigation Buttons */}
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <button
            type="button"
            onClick={goPrev}
            className="btn-ghost"
            disabled={currentStep === 0}
            style={{ visibility: currentStep === 0 ? 'hidden' : 'visible' }}
          >
            ← السابق
          </button>

          <div style={{ display: 'flex', gap: 8 }}>
            {currentStep < STEPS.length - 1 ? (
              <button type="button" onClick={goNext} className="btn-primary">
                التالي →
              </button>
            ) : (
              <button
                id="invoice-submit-btn"
                type="button"
                disabled={busy}
                onClick={() => void submit()}
                className="btn-primary"
              >
                {busy ? (
                  <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <span className="vex-spinner" style={{ width: 16, height: 16, borderWidth: 2 }} />
                    جارٍ الحفظ...
                  </span>
                ) : '💾 حفظ الفاتورة (مسودة)'}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

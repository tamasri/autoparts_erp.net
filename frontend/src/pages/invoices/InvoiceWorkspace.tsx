import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { invoicesApi, type CreateInvoice } from '../../api/endpoints/invoices';
import { customersApi } from '../../api/endpoints/customers';
import { lookupsApi, type PickItem } from '../../api/endpoints/lookups';
import { unwrapNode, unwrapPaged } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import EntityPicker, { type PickerOption } from '../../components/pickers/EntityPicker';
import FxRateField, { type FxRate } from '../../components/pickers/FxRateField';
import ItemPickerModal, { type PickedLine } from '../../components/pickers/ItemPickerModal';

type CustomerRecord = {
  id: string;
  code?: string;
  name?: string;
  phone?: string;
  paymentTermsDays?: number;
  creditLimitSyp?: number;
  creditLimitUsd?: number;
  assignedSalesRep?: string | null;
};

/** One invoice line as edited on screen: the payload fields plus what the picker already knows about the item. */
type Line = {
  key: string;
  skuId: string;
  code: string;
  name: string;
  locationId: string;
  batchId: string;
  quantity: number;
  unitPriceSyp: number;
  unitPriceUsd: number;
  discountPct: number;
  overrideReason: string;
  minPriceSyp: number;
  minPriceUsd: number;
  item: PickItem;
};

const fmt = (v: number): string => Number(v ?? 0).toLocaleString('en-US');
// Local calendar date (toISOString() is UTC and shifts the day for users ahead of/behind UTC).
const toLocalDate = (d: Date): string => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const today = toLocalDate(new Date());

function extractError(e: unknown, fallback: string): string {
  const r = e as { response?: { data?: { detail?: string; message?: string } } };
  return r.response?.data?.detail ?? r.response?.data?.message ?? fallback;
}

const addDays = (date: string, days: number): string => {
  const d = new Date(`${date}T00:00:00`);
  d.setDate(d.getDate() + days);
  return toLocalDate(d);
};

const STEPS = [
  { label: 'بيانات الفاتورة', sublabel: 'الزبون والتاريخ وسعر الصرف' },
  { label: 'الأسطر', sublabel: 'الأصناف والكميات' },
  { label: 'المراجعة', sublabel: 'التحقق والحفظ' },
];

function StepCircle({ index, currentStep }: { index: number; currentStep: number }): JSX.Element {
  const state = index < currentStep ? 'completed' : index === currentStep ? 'active' : 'inactive';
  return (
    <div className={`vex-stepper__item vex-stepper__item--${state}`}>
      <div className="vex-stepper__circle">{state === 'completed' ? '✓' : index + 1}</div>
      <div className="vex-stepper__label">
        <div style={{ fontWeight: 600 }}>{STEPS[index].label}</div>
        <div style={{ fontSize: 11, marginTop: 2, opacity: 0.7 }}>{STEPS[index].sublabel}</div>
      </div>
    </div>
  );
}

const availableFor = (l: Line): number => {
  if (l.item.isBatchTracked && l.batchId) return l.item.batches.find((b) => b.id === l.batchId)?.quantity ?? 0;
  return l.item.stock.find((s) => s.locationId === l.locationId)?.available ?? 0;
};

const belowMinimum = (l: Line): boolean =>
  (l.minPriceSyp > 0 && l.unitPriceSyp < l.minPriceSyp) || (l.minPriceUsd > 0 && l.unitPriceUsd < l.minPriceUsd);

const lineTotals = (l: Line): { syp: number; usd: number } => {
  const factor = Number(l.quantity) * (1 - Number(l.discountPct) / 100);
  return { syp: factor * Number(l.unitPriceSyp), usd: factor * Number(l.unitPriceUsd) };
};

export default function InvoiceWorkspace(): JSX.Element {
  const navigate = useNavigate();
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [currentStep, setCurrentStep] = useState(0);

  const [customer, setCustomer] = useState<PickerOption | null>(null);
  const [invoiceDate, setInvoiceDate] = useState(today);
  const [dueDate, setDueDate] = useState(today);
  const [dueTouched, setDueTouched] = useState(false);
  const [fxRateId, setFxRateId] = useState('');
  const [fxRate, setFxRate] = useState<FxRate | null>(null);
  const [invoiceType, setInvoiceType] = useState('SALE');
  const [deliveryFeeSyp, setDeliveryFeeSyp] = useState(0);
  const [deliveryFeeUsd, setDeliveryFeeUsd] = useState(0);
  const [lines, setLines] = useState<Line[]>([]);

  const [pickerOpen, setPickerOpen] = useState(false);
  const [pickerSearch, setPickerSearch] = useState('');
  const [scan, setScan] = useState('');
  const [scanNote, setScanNote] = useState('');

  const isReturn = invoiceType === 'RETURN';
  const customerRecord = customer?.data as CustomerRecord | undefined;

  const searchCustomers = useCallback(async (text: string): Promise<PickerOption[]> => {
    const res = await customersApi.getCustomers({ page: 1, pageSize: 10, searchTerm: text || undefined, isActive: true });
    return unwrapPaged<CustomerRecord>(res.data).items.map((c) => ({
      id: c.id,
      label: c.name ?? c.id.slice(0, 8),
      sublabel: [c.code, c.phone, c.paymentTermsDays ? `استحقاق ${c.paymentTermsDays} يوم` : ''].filter(Boolean).join(' · '),
      data: c,
    }));
  }, []);

  // The due date follows the customer's payment terms until the user edits it by hand.
  useEffect(() => {
    if (dueTouched) return;
    const terms = customerRecord?.paymentTermsDays ?? 0;
    setDueDate(addDays(invoiceDate, terms > 0 ? terms : 0));
  }, [invoiceDate, customerRecord, dueTouched]);

  // F2 opens the item picker while editing lines.
  useEffect(() => {
    if (currentStep !== 1) return undefined;
    const onKey = (e: KeyboardEvent): void => {
      if (e.key === 'F2') { e.preventDefault(); setPickerSearch(''); setPickerOpen(true); }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [currentStep]);

  const addFromPick = useCallback((p: PickedLine): void => {
    if (!p.skuId) return;
    setError('');
    setLines((prev) => {
      const existing = prev.findIndex((l) => l.skuId === p.skuId && l.locationId === p.locationId && l.batchId === (p.batchId ?? ''));
      if (existing >= 0) {
        return prev.map((l, i) => (i === existing ? { ...l, quantity: Number(l.quantity) + p.quantity } : l));
      }
      return [...prev, {
        key: `${p.skuId}-${Date.now()}-${prev.length}`,
        skuId: p.skuId as string,
        code: p.code,
        name: p.nameAr || p.name,
        locationId: p.locationId,
        batchId: p.batchId ?? '',
        quantity: p.quantity,
        unitPriceSyp: p.unitPriceSyp,
        unitPriceUsd: p.unitPriceUsd,
        discountPct: 0,
        overrideReason: '',
        minPriceSyp: p.minPriceSyp,
        minPriceUsd: p.minPriceUsd,
        item: p.item,
      }];
    });
  }, []);

  function patchLine(key: string, patch: Partial<Line>): void {
    setLines((prev) => prev.map((l) => {
      if (l.key !== key) return l;
      const next = { ...l, ...patch };
      if (patch.locationId !== undefined && patch.locationId !== l.locationId) {
        next.batchId = l.item.isBatchTracked ? (l.item.batches.find((b) => b.locationId === patch.locationId)?.id ?? '') : '';
      }
      return next;
    }));
  }

  async function handleScan(): Promise<void> {
    const code = scan.trim();
    if (!code) return;
    setScanNote('');
    try {
      const res = await lookupsApi.pickItems({ search: code, mode: 'sales', inStockOnly: !isReturn, page: 1, pageSize: 2 });
      const found = unwrapPaged<PickItem>(res.data);
      if (found.items.length === 1) {
        const it = found.items[0];
        const best = [...it.stock].filter((s) => isReturn || s.available > 0).sort((a, b) => b.available - a.available)[0];
        const batch = it.isBatchTracked ? it.batches.find((b) => b.locationId === best?.locationId) : undefined;
        if (!it.skuId || it.isStopShip || !best || (it.isBatchTracked && !batch && !isReturn)) {
          setScanNote(it.isStopShip ? `الصنف ${it.code} موقوف الشحن` : `تعذّر إضافة ${it.code} تلقائياً — اختره من النافذة`);
          setPickerSearch(code); setPickerOpen(true);
        } else {
          addFromPick({
            itemId: it.itemId, skuId: it.skuId, code: it.code, name: it.name, nameAr: it.nameAr,
            locationId: best.locationId, locationCode: best.locationCode, batchId: batch?.id, batchNumber: batch?.batchNumber,
            quantity: 1, unitPriceSyp: it.sellingPriceSyp, unitPriceUsd: it.sellingPriceUsd,
            minPriceSyp: it.minSellingPriceSyp, minPriceUsd: it.minSellingPriceUsd, available: best.available,
            isBatchTracked: it.isBatchTracked, item: it,
          });
          setScanNote(`✓ أُضيف ${it.code}`);
        }
      } else {
        setPickerSearch(code);
        setPickerOpen(true);
      }
      setScan('');
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر البحث عن الصنف'));
    }
  }

  const totals = useMemo(() => lines.reduce((acc, l) => {
    const t = lineTotals(l);
    return { syp: acc.syp + t.syp, usd: acc.usd + t.usd };
  }, { syp: Number(deliveryFeeSyp), usd: Number(deliveryFeeUsd) }), [lines, deliveryFeeSyp, deliveryFeeUsd]);

  // The credit limit is kept in dollars; what the customer already owes comes from their statement.
  const [owedUsd, setOwedUsd] = useState(0);
  useEffect(() => {
    if (!customerRecord?.id) { setOwedUsd(0); return; }
    let live = true;
    customersApi.getCustomerStatement(customerRecord.id)
      .then((r) => { if (live) setOwedUsd(Number(unwrapNode<{ outstandingUsd?: number }>(r.data)?.outstandingUsd ?? 0)); })
      .catch(() => { if (live) setOwedUsd(0); });
    return () => { live = false; };
  }, [customerRecord?.id]);

  const creditWarning = useMemo(() => {
    const limit = Number(customerRecord?.creditLimitUsd ?? 0);
    if (!customerRecord || limit <= 0 || isReturn) return '';
    const projected = owedUsd + totals.usd;
    return projected > limit ? `تجاوز الحد الائتماني: الرصيد بعد الفاتورة ${fmt(projected)} من أصل ${fmt(limit)}` : '';
  }, [customerRecord, owedUsd, totals.usd, isReturn]);

  function lineProblems(): string {
    if (lines.length === 0) return 'أضف صنفاً واحداً على الأقل';
    for (const l of lines) {
      if (!(Number(l.quantity) > 0)) return `الكمية غير صحيحة للصنف ${l.code}`;
      if (!l.locationId) return `اختر الموقع للصنف ${l.code}`;
      if (!isReturn && Number(l.quantity) > availableFor(l)) return `الكمية تتجاوز المتاح للصنف ${l.code} (${fmt(availableFor(l))})`;
      if (!isReturn && l.item.isBatchTracked && !l.batchId) return `اختر الدفعة للصنف ${l.code}`;
      if (belowMinimum(l) && !l.overrideReason.trim()) return `السعر أقل من الحد الأدنى للصنف ${l.code} — اكتب سبب التجاوز`;
    }
    return '';
  }

  function goNext(): void {
    if (currentStep === 0) {
      if (!customer) { setError('اختر الزبون'); return; }
      if (!fxRateId) { setError('لا يوجد سعر صرف — أضف سعراً من الإعدادات'); return; }
    }
    if (currentStep === 1) {
      const problem = lineProblems();
      if (problem) { setError(problem); return; }
    }
    setError('');
    setCurrentStep((s) => Math.min(s + 1, STEPS.length - 1));
  }

  async function submit(): Promise<void> {
    const problem = !customer ? 'اختر الزبون' : !fxRateId ? 'لا يوجد سعر صرف' : lineProblems();
    if (problem) { setError(problem); return; }
    setBusy(true);
    setError('');
    try {
      const payload: CreateInvoice = {
        customerId: (customer as PickerOption).id,
        invoiceDate,
        dueDate,
        fxRateId,
        invoiceType,
        salesRepId: customerRecord?.assignedSalesRep || undefined,
        deliveryFeeSyp: Number(deliveryFeeSyp),
        deliveryFeeUsd: Number(deliveryFeeUsd),
        lines: lines.map((l) => ({
          skuId: l.skuId,
          batchId: l.batchId || undefined,
          locationId: l.locationId,
          quantity: Number(l.quantity),
          unitPriceSyp: Number(l.unitPriceSyp),
          unitPriceUsd: Number(l.unitPriceUsd),
          discountPct: Number(l.discountPct),
          isPriceOverride: belowMinimum(l),
          overrideReason: belowMinimum(l) ? l.overrideReason.trim() : undefined,
        })),
      };
      const res = await invoicesApi.createInvoice(payload);
      const created = unwrapNode<{ id?: string }>(res.data);
      navigate(created?.id ? `/invoices/${created.id}` : '/invoices');
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر إنشاء الفاتورة'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">{isReturn ? 'مرتجع جديد' : 'فاتورة جديدة'}</h1>
          <div className="vex-page-header__breadcrumb">
            <Link to="/invoices" style={{ color: 'var(--clr-primary)', textDecoration: 'none' }}>الفواتير</Link>{' / '}إنشاء فاتورة جديدة
          </div>
        </div>
        <button type="button" onClick={() => navigate('/invoices')} className="btn-ghost">← رجوع</button>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      <div className="vex-card">
        <div className="vex-stepper">
          {STEPS.map((_, i) => <StepCircle key={i} index={i} currentStep={currentStep} />)}
        </div>
        <hr className="vex-divider" />

        {/* ── Step 0: header ── */}
        {currentStep === 0 && (
          <div>
            <h2 className="vex-section-title" style={{ marginBottom: 24 }}>المعلومات الأساسية</h2>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))', gap: 20 }}>
              <label className="vex-label" style={{ gridColumn: 'span 2' }}>
                الزبون *
                <EntityPicker id="invoice-customer" value={customer} onChange={setCustomer} search={searchCustomers} placeholder="ابحث بالاسم أو الكود أو الهاتف..." />
              </label>

              <label className="vex-label">
                نوع الفاتورة
                <select id="invoice-type" value={invoiceType} onChange={(e) => { setInvoiceType(e.target.value); setLines([]); }} className="vex-select">
                  <option value="SALE">بيع</option>
                  <option value="RETURN">مرتجع</option>
                </select>
              </label>

              <label className="vex-label">
                تاريخ الفاتورة
                <input id="invoice-date" type="date" value={invoiceDate} onChange={(e) => setInvoiceDate(e.target.value)} className="vex-input" />
              </label>

              <label className="vex-label">
                تاريخ الاستحقاق {customerRecord?.paymentTermsDays ? <span style={{ color: 'var(--txt-muted)', fontWeight: 400 }}>(شروط الزبون: {customerRecord.paymentTermsDays} يوم)</span> : null}
                <input id="invoice-due-date" type="date" value={dueDate} onChange={(e) => { setDueDate(e.target.value); setDueTouched(true); }} className="vex-input" />
              </label>

              <div className="vex-label" style={{ gridColumn: 'span 2' }}>
                سعر الصرف
                <FxRateField value={fxRateId} onChange={(id, rate) => { setFxRateId(id); setFxRate(rate); }} documentDate={invoiceDate} />
              </div>

              <label className="vex-label">
                رسوم التوصيل ل.س
                <input id="invoice-delivery-syp" type="number" min={0} value={deliveryFeeSyp} onChange={(e) => setDeliveryFeeSyp(Number(e.target.value))} className="vex-input" />
              </label>
              <label className="vex-label">
                رسوم التوصيل $
                <input id="invoice-delivery-usd" type="number" min={0} value={deliveryFeeUsd} onChange={(e) => setDeliveryFeeUsd(Number(e.target.value))} className="vex-input" />
              </label>
            </div>
          </div>
        )}

        {/* ── Step 1: lines ── */}
        {currentStep === 1 && (
          <div>
            <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', alignItems: 'center', marginBottom: 16 }}>
              <button type="button" className="btn-primary" onClick={() => { setPickerSearch(''); setPickerOpen(true); }}>🔍 اختيار أصناف (F2)</button>
              <input
                className="vex-input"
                style={{ flex: '1 1 260px', maxWidth: 420 }}
                value={scan}
                placeholder="امسح الباركود أو اكتب الكود ثم Enter"
                onChange={(e) => setScan(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); void handleScan(); } }}
              />
              {scanNote ? <span style={{ fontSize: 13, color: scanNote.startsWith('✓') ? 'var(--clr-success)' : 'var(--clr-warning)' }}>{scanNote}</span> : null}
            </div>

            {lines.length === 0 ? (
              <div style={{ textAlign: 'center', padding: '40px 0', color: 'var(--txt-muted)', border: '1px dashed var(--clr-border)', borderRadius: 'var(--radius-md)' }}>
                لا توجد أصناف بعد — اضغط «اختيار أصناف» أو امسح الباركود
              </div>
            ) : (
              <div style={{ overflowX: 'auto' }}>
                <table className="vex-table" style={{ minWidth: 1040 }}>
                  <thead>
                    <tr>
                      <th>#</th><th>الصنف</th><th>الموقع</th>{!isReturn ? <th>الدفعة</th> : null}<th style={{ width: 100 }}>الكمية</th>
                      <th style={{ width: 120 }}>السعر ل.س</th><th style={{ width: 100 }}>السعر $</th><th style={{ width: 80 }}>خصم %</th><th>الإجمالي</th><th />
                    </tr>
                  </thead>
                  <tbody>
                    {lines.map((l, idx) => {
                      const avail = availableFor(l);
                      const over = !isReturn && Number(l.quantity) > avail;
                      const below = belowMinimum(l);
                      const t = lineTotals(l);
                      const stockOpts = l.item.stock.filter((s) => isReturn || s.available > 0 || s.locationId === l.locationId);
                      const batches = l.item.batches.filter((b) => b.locationId === l.locationId);
                      return (
                        <tr key={l.key}>
                          <td>{idx + 1}</td>
                          <td>
                            <div style={{ fontFamily: 'monospace', fontWeight: 700, color: 'var(--clr-primary)' }}>{l.code}</div>
                            <div style={{ fontSize: 13 }}>{l.name}</div>
                            {below ? (
                              <input
                                className="vex-input"
                                style={{ marginTop: 6, fontSize: 12, borderColor: 'var(--clr-warning)' }}
                                placeholder={`سعر أقل من الحد الأدنى — سبب التجاوز *`}
                                value={l.overrideReason}
                                onChange={(e) => patchLine(l.key, { overrideReason: e.target.value })}
                              />
                            ) : null}
                          </td>
                          <td>
                            <select className="vex-select" value={l.locationId} onChange={(e) => patchLine(l.key, { locationId: e.target.value })}>
                              {stockOpts.map((s) => <option key={s.locationId} value={s.locationId}>{s.locationCode} ({fmt(s.available)})</option>)}
                            </select>
                          </td>
                          {!isReturn ? (
                            <td>
                              {l.item.isBatchTracked ? (
                                <select className="vex-select" value={l.batchId} onChange={(e) => patchLine(l.key, { batchId: e.target.value })}>
                                  <option value="">— اختر —</option>
                                  {batches.map((b) => <option key={b.id} value={b.id}>{b.batchNumber} ({fmt(b.quantity)})</option>)}
                                </select>
                              ) : <span style={{ color: 'var(--txt-muted)' }}>—</span>}
                            </td>
                          ) : null}
                          <td>
                            <input type="number" min={0} className="vex-input" style={over ? { borderColor: 'var(--clr-danger)' } : undefined} value={l.quantity} onChange={(e) => patchLine(l.key, { quantity: Number(e.target.value) })} />
                            {!isReturn ? <div style={{ fontSize: 11, color: over ? 'var(--clr-danger)' : 'var(--txt-muted)' }}>متاح {fmt(avail)}</div> : null}
                          </td>
                          <td><input type="number" min={0} className="vex-input" value={l.unitPriceSyp} onChange={(e) => patchLine(l.key, { unitPriceSyp: Number(e.target.value) })} /></td>
                          <td><input type="number" min={0} className="vex-input" value={l.unitPriceUsd} onChange={(e) => patchLine(l.key, { unitPriceUsd: Number(e.target.value) })} /></td>
                          <td><input type="number" min={0} max={100} className="vex-input" value={l.discountPct} onChange={(e) => patchLine(l.key, { discountPct: Number(e.target.value) })} /></td>
                          <td style={{ whiteSpace: 'nowrap' }}>
                            <div style={{ fontWeight: 700 }}>{fmt(t.syp)} ل.س</div>
                            <div style={{ fontSize: 12, color: 'var(--txt-secondary)' }}>${fmt(t.usd)}</div>
                          </td>
                          <td><button type="button" className="btn-danger" style={{ padding: '4px 10px', fontSize: 12 }} onClick={() => setLines((prev) => prev.filter((x) => x.key !== l.key))}>✕</button></td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}

            <div style={{ marginTop: 20, padding: '16px 20px', background: 'var(--clr-primary-light)', borderRadius: 'var(--radius-md)', display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8 }}>
              <span style={{ fontSize: 14, fontWeight: 600, color: 'var(--clr-primary-dark)' }}>الإجمالي التقديري (شامل رسوم التوصيل)</span>
              <span style={{ fontSize: 22, fontWeight: 800, color: 'var(--clr-primary)' }}>
                {fmt(totals.syp)} <span style={{ fontSize: 14, fontWeight: 500 }}>ل.س</span>
                <span style={{ fontSize: 14, fontWeight: 600, marginInlineStart: 14 }}>${fmt(totals.usd)}</span>
              </span>
            </div>
            {creditWarning ? <div className="badge badge--warning" style={{ marginTop: 10 }}>⚠ {creditWarning}</div> : null}
          </div>
        )}

        {/* ── Step 2: review ── */}
        {currentStep === 2 && (
          <div>
            <h2 className="vex-section-title" style={{ marginBottom: 24 }}>مراجعة الفاتورة</h2>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: 16, marginBottom: 24 }}>
              <div style={{ background: 'var(--clr-surface-2)', border: '1px solid var(--clr-border)', borderRadius: 'var(--radius-md)', padding: '16px 20px' }}>
                <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--txt-muted)', marginBottom: 10 }}>بيانات الفاتورة</div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 8, fontSize: 13 }}>
                  <Row label="الزبون" value={customer?.label ?? '-'} />
                  <Row label="النوع" value={isReturn ? 'مرتجع' : 'بيع'} />
                  <Row label="التاريخ" value={invoiceDate} />
                  <Row label="الاستحقاق" value={dueDate} />
                  <Row label="سعر الصرف" value={fxRate ? `${fmt(fxRate.midRate)} ${fxRate.currencyTo} (${fxRate.rateDate})` : '-'} />
                </div>
              </div>
              <div style={{ background: 'var(--clr-primary-light)', border: '1px solid #c7c4ff', borderRadius: 'var(--radius-md)', padding: '16px 20px' }}>
                <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--clr-primary-dark)', marginBottom: 10 }}>الملخص المالي</div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 8, fontSize: 13 }}>
                  <Row label="عدد الأسطر" value={String(lines.length)} />
                  <Row label="رسوم التوصيل" value={`${fmt(deliveryFeeSyp)} ل.س · $${fmt(deliveryFeeUsd)}`} />
                  <div style={{ display: 'flex', justifyContent: 'space-between', borderTop: '1px dashed #a5a0ff', paddingTop: 8, marginTop: 4 }}>
                    <span style={{ fontWeight: 700, color: 'var(--clr-primary-dark)' }}>الإجمالي</span>
                    <span style={{ fontWeight: 800, color: 'var(--clr-primary)', fontSize: 16 }}>{fmt(totals.syp)} ل.س · ${fmt(totals.usd)}</span>
                  </div>
                </div>
              </div>
            </div>

            <div style={{ overflowX: 'auto', marginBottom: 16 }}>
              <table className="vex-table">
                <thead><tr><th>الصنف</th><th>الموقع</th><th>الكمية</th><th>السعر ل.س</th><th>الإجمالي ل.س</th></tr></thead>
                <tbody>
                  {lines.map((l) => (
                    <tr key={l.key}>
                      <td><span style={{ fontFamily: 'monospace', fontWeight: 700 }}>{l.code}</span> <span style={{ color: 'var(--txt-secondary)' }}>{l.name}</span></td>
                      <td>{l.item.stock.find((s) => s.locationId === l.locationId)?.locationCode ?? '-'}</td>
                      <td>{fmt(l.quantity)}</td>
                      <td>{fmt(l.unitPriceSyp)}</td>
                      <td style={{ fontWeight: 600 }}>{fmt(lineTotals(l).syp)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {creditWarning ? <div className="badge badge--warning" style={{ marginBottom: 12 }}>⚠ {creditWarning}</div> : null}
            <div style={{ background: 'var(--clr-warning-light)', border: '1px solid #fde68a', borderRadius: 'var(--radius-md)', padding: '12px 16px', fontSize: 13, color: '#92400e' }}>
              <strong>ملاحظة:</strong> ستُحفظ الفاتورة كمسودة. عند ترحيلها تُرسل تلقائياً إلى دفتر الأستاذ (ERPNext) وتظهر حالتها في «مزامنة المحاسبة».
            </div>
          </div>
        )}

        <hr className="vex-divider" />

        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <button type="button" onClick={() => { setError(''); setCurrentStep((s) => Math.max(s - 1, 0)); }} className="btn-ghost" style={{ visibility: currentStep === 0 ? 'hidden' : 'visible' }}>← السابق</button>
          <div style={{ display: 'flex', gap: 8 }}>
            {currentStep < STEPS.length - 1 ? (
              <button type="button" onClick={goNext} className="btn-primary">التالي →</button>
            ) : (
              <button id="invoice-submit-btn" type="button" disabled={busy} onClick={() => void submit()} className="btn-primary">
                {busy ? 'جارٍ الحفظ...' : '💾 حفظ الفاتورة (مسودة)'}
              </button>
            )}
          </div>
        </div>
      </div>

      <ItemPickerModal
        open={pickerOpen}
        mode="sales"
        enforceStock={!isReturn}
        initialSearch={pickerSearch}
        title={isReturn ? 'اختيار أصناف المرتجع' : 'اختيار الأصناف للفاتورة'}
        onPick={addFromPick}
        onClose={() => setPickerOpen(false)}
      />
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }): JSX.Element {
  return (
    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}>
      <span style={{ color: 'var(--txt-muted)' }}>{label}</span>
      <span style={{ fontWeight: 600, textAlign: 'left' }}>{value}</span>
    </div>
  );
}

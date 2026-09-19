import { useCallback, useState } from 'react';
import { paymentsApi, type AllocationLine } from '../../api/endpoints/payments';
import { customersApi } from '../../api/endpoints/customers';
import { invoicesApi } from '../../api/endpoints/invoices';
import { unwrapNode, unwrapPaged } from '../../api/apiData';
import { usePagedList } from '../../hooks/usePagedList';
import { toast, extractApiError } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';
import Pagination from '../../components/common/Pagination';
import ErrorBanner from '../../components/common/ErrorBanner';
import EntityPicker, { type PickerOption } from '../../components/pickers/EntityPicker';
import FxRateField from '../../components/pickers/FxRateField';

type Payment = {
  id: string; paymentNumber: string; paymentType: string; customerId: string; customerName: string; paymentDate: string;
  paymentMethod: string; amountSyp: number; amountUsd: number; unallocatedSyp: number; unallocatedUsd: number; isReversed: boolean;
};
type CustomerRow = { id: string; code?: string; name?: string; phone?: string };
type OpenInvoice = { id: string; invoiceNumber: string; balanceSyp: number; balanceUsd: number; dueDate: string };

const METHODS = [
  { value: 'CASH', label: 'نقداً (ل.س)' },
  { value: 'USD_CASH', label: 'نقداً (دولار)' },
  { value: 'BANK_TRANSFER', label: 'حوالة مصرفية' },
  { value: 'CHEQUE', label: 'شيك' },
];
const methodLabel = (m: string): string => METHODS.find((x) => x.value === m)?.label ?? m;
const money = (v: number): string => Number(v ?? 0).toLocaleString('en-US');
const today = (): string => new Date().toLocaleDateString('en-CA');

/** Oldest-first allocation of a receipt over the customer's open invoices, per currency. */
function autoAllocate(invoices: OpenInvoice[], amountSyp: number, amountUsd: number): AllocationLine[] {
  let syp = amountSyp; let usd = amountUsd;
  const out: AllocationLine[] = [];
  for (const inv of [...invoices].sort((a, b) => a.dueDate.localeCompare(b.dueDate))) {
    if (syp <= 0 && usd <= 0) break;
    const s = Math.min(syp, Math.max(inv.balanceSyp, 0));
    const u = Math.min(usd, Math.max(inv.balanceUsd, 0));
    if (s > 0 || u > 0) { out.push({ invoiceId: inv.id, allocatedSyp: s, allocatedUsd: u }); syp -= s; usd -= u; }
  }
  return out;
}

export default function Payments(): JSX.Element {
  const [filterCustomer, setFilterCustomer] = useState<PickerOption | null>(null);
  const [filterMethod, setFilterMethod] = useState('');
  const list = usePagedList<Payment>({
    errorMessage: 'تعذر تحميل الدفعات',
    deps: [filterCustomer?.id, filterMethod],
    fetcher: ({ page, pageSize }) => paymentsApi.list({ page, pageSize, customerId: filterCustomer?.id, paymentMethod: filterMethod || undefined }),
  });

  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState('');
  const [formError, setFormError] = useState('');
  const [customer, setCustomer] = useState<PickerOption | null>(null);
  const [method, setMethod] = useState('CASH');
  const [paymentDate, setPaymentDate] = useState(today());
  const [amount, setAmount] = useState(0);
  const [fxRateId, setFxRateId] = useState('');
  const [sellRate, setSellRate] = useState(0);
  const [reference, setReference] = useState('');
  const [bankName, setBankName] = useState('');
  const [chequeNumber, setChequeNumber] = useState('');
  const [chequeDate, setChequeDate] = useState('');
  const [notes, setNotes] = useState('');
  const [openInvoices, setOpenInvoices] = useState<OpenInvoice[]>([]);
  const [autoApply, setAutoApply] = useState(true);
  const [reverseId, setReverseId] = useState('');
  const [reverseReason, setReverseReason] = useState('');

  const isUsd = method === 'USD_CASH';
  const amountSyp = isUsd ? 0 : amount;
  const amountUsd = isUsd ? amount : 0;

  const searchCustomers = useCallback(async (text: string): Promise<PickerOption[]> => {
    const res = await customersApi.getCustomers({ page: 1, pageSize: 10, searchTerm: text || undefined, isActive: true });
    return unwrapPaged<CustomerRow>(res.data).items.map((c) => ({ id: c.id, label: c.name ?? c.id.slice(0, 8), sublabel: [c.code, c.phone].filter(Boolean).join(' · ') }));
  }, []);

  async function pickCustomer(c: PickerOption | null): Promise<void> {
    setCustomer(c); setOpenInvoices([]);
    if (!c) return;
    try {
      const res = await invoicesApi.getInvoices({ page: 1, pageSize: 100, status: 'POSTED', customerId: c.id });
      setOpenInvoices(unwrapPaged<OpenInvoice>(res.data).items.filter((i) => i.balanceSyp > 0 || i.balanceUsd > 0));
    } catch { /* balances are informational; allocation is retried server-side */ }
  }

  const plan = autoApply ? autoAllocate(openInvoices, amountSyp, amountUsd) : [];
  const invName = (id: string): string => openInvoices.find((i) => i.id === id)?.invoiceNumber ?? id.slice(0, 8);

  async function create(): Promise<void> {
    if (!customer) { setFormError('اختر العميل'); return; }
    if (!(amount > 0)) { setFormError('أدخل مبلغاً أكبر من صفر'); return; }
    if (!fxRateId) { setFormError('لا يوجد سعر صرف — أضفه من شاشة أسعار الصرف'); return; }
    if (method === 'CHEQUE' && !chequeNumber.trim()) { setFormError('رقم الشيك مطلوب'); return; }
    setBusy('create'); setFormError('');
    try {
      const res = await paymentsApi.create({
        paymentType: 'RECEIPT', customerId: customer.id, paymentDate, paymentMethod: method, amountSyp, amountUsd, fxRateId,
        referenceNumber: reference.trim() || undefined, bankName: bankName.trim() || undefined,
        chequeNumber: chequeNumber.trim() || undefined, chequeDate: chequeDate || undefined, notes: notes.trim() || undefined,
      });
      if (notifyResult(res, 'تم تسجيل الدفعة') === 'done' && plan.length > 0) {
        const created = unwrapNode<{ id?: string }>(res.data);
        if (created?.id) {
          try { await paymentsApi.allocate(created.id, plan); toast.success(`وُزّعت الدفعة على ${plan.length} فاتورة`); }
          catch (e: unknown) { toast.error(extractApiError(e, 'سُجّلت الدفعة لكن تعذر توزيعها على الفواتير')); }
        }
      }
      setCustomer(null); setAmount(0); setReference(''); setBankName(''); setChequeNumber(''); setChequeDate(''); setNotes(''); setOpenInvoices([]);
      setShowForm(false); list.reload();
    } catch (e: unknown) { setFormError(extractApiError(e, 'تعذر تسجيل الدفعة')); }
    finally { setBusy(''); }
  }

  async function reverse(): Promise<void> {
    if (reverseReason.trim().length < 5) { toast.error('اكتب سبب العكس (5 أحرف على الأقل)'); return; }
    setBusy(reverseId);
    try {
      const res = await paymentsApi.reverse(reverseId, reverseReason.trim());
      notifyResult(res, 'تم عكس الدفعة وإعادة الرصيد للفواتير');
      setReverseId(''); setReverseReason(''); list.reload();
    } catch (e: unknown) { toast.error(extractApiError(e, 'تعذر عكس الدفعة')); }
    finally { setBusy(''); }
  }

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">الدفعات والقبض</h1>
          <div className="vex-page-header__breadcrumb">سندات قبض العملاء وتوزيعها على الفواتير المفتوحة</div>
        </div>
        <button type="button" onClick={() => setShowForm((s) => !s)} className={showForm ? 'btn-ghost' : 'btn-primary'}>
          {showForm ? '✕ إلغاء' : '＋ سند قبض'}
        </button>
      </div>

      {list.error ? <ErrorBanner message={list.error} /> : null}

      {showForm ? (
        <div className="vex-card" style={{ marginBottom: 20 }}>
          <h2 className="vex-section-title">سند قبض جديد</h2>
          {formError ? <ErrorBanner message={formError} /> : null}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: 16, marginBottom: 16 }}>
            <label className="vex-label">العميل *<EntityPicker value={customer} onChange={(c) => void pickCustomer(c)} search={searchCustomers} placeholder="ابحث بالاسم أو الكود أو الهاتف..." /></label>
            <label className="vex-label">طريقة الدفع
              <select value={method} onChange={(e) => setMethod(e.target.value)} className="vex-select">{METHODS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}</select>
            </label>
            <label className="vex-label">المبلغ ({isUsd ? 'USD' : 'ل.س'}) *<input type="number" min={0} value={amount || ''} onChange={(e) => setAmount(Number(e.target.value))} className="vex-input" /></label>
            <label className="vex-label">تاريخ الدفعة<input type="date" value={paymentDate} onChange={(e) => setPaymentDate(e.target.value)} className="vex-input" /></label>
            {method === 'BANK_TRANSFER' ? (
              <>
                <label className="vex-label">المصرف<input value={bankName} onChange={(e) => setBankName(e.target.value)} className="vex-input" /></label>
                <label className="vex-label">رقم الحوالة<input value={reference} onChange={(e) => setReference(e.target.value)} className="vex-input" /></label>
              </>
            ) : null}
            {method === 'CHEQUE' ? (
              <>
                <label className="vex-label">رقم الشيك *<input value={chequeNumber} onChange={(e) => setChequeNumber(e.target.value)} className="vex-input" /></label>
                <label className="vex-label">تاريخ الشيك<input type="date" value={chequeDate} onChange={(e) => setChequeDate(e.target.value)} className="vex-input" /></label>
                <label className="vex-label">المصرف<input value={bankName} onChange={(e) => setBankName(e.target.value)} className="vex-input" /></label>
              </>
            ) : null}
            <label className="vex-label">ملاحظات<input value={notes} onChange={(e) => setNotes(e.target.value)} className="vex-input" /></label>
          </div>

          <div style={{ marginBottom: 16 }}>
            <FxRateField value={fxRateId} onChange={(id, r) => { setFxRateId(id); setSellRate(r?.midRate ?? 0); }} documentDate={paymentDate} />
            {amount > 0 && sellRate > 0 ? (
              <div style={{ fontSize: 12, color: 'var(--txt-muted)', marginTop: 6 }}>
                {isUsd ? `≈ ${money(amount * sellRate)} ل.س` : `≈ ${money(amount / sellRate)} USD`} بسعر {money(sellRate)}
              </div>
            ) : null}
          </div>

          {customer ? (
            <div style={{ marginBottom: 16 }}>
              <label style={{ display: 'flex', gap: 8, alignItems: 'center', fontSize: 13, marginBottom: 8 }}>
                <input type="checkbox" checked={autoApply} onChange={(e) => setAutoApply(e.target.checked)} />
                توزيع تلقائي على الفواتير المفتوحة (الأقدم استحقاقاً أولاً)
              </label>
              {openInvoices.length === 0 ? (
                <div style={{ fontSize: 13, color: 'var(--txt-muted)' }}>لا توجد فواتير مرحّلة بأرصدة مفتوحة لهذا العميل — ستُسجَّل الدفعة كرصيد دائن غير موزّع.</div>
              ) : (
                <table className="vex-table">
                  <thead><tr><th>الفاتورة</th><th>الاستحقاق</th><th>الرصيد (ل.س)</th><th>الرصيد ($)</th><th>سيُسدَّد</th></tr></thead>
                  <tbody>
                    {openInvoices.map((i) => {
                      const p = plan.find((x) => x.invoiceId === i.id);
                      return (
                        <tr key={i.id}>
                          <td style={{ fontWeight: 600 }}>{i.invoiceNumber}</td><td>{i.dueDate}</td><td>{money(i.balanceSyp)}</td><td>{money(i.balanceUsd)}</td>
                          <td style={{ color: 'var(--clr-success)', fontWeight: 700 }}>{p ? (p.allocatedSyp > 0 ? `${money(p.allocatedSyp)} ل.س ` : '') + (p.allocatedUsd > 0 ? `${money(p.allocatedUsd)} $` : '') : '—'}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              )}
              {plan.length > 0 ? <div style={{ fontSize: 12, color: 'var(--txt-muted)', marginTop: 6 }}>{plan.map((p) => invName(p.invoiceId)).join('، ')}</div> : null}
            </div>
          ) : null}

          <button type="button" disabled={busy === 'create'} onClick={() => void create()} className="btn-primary">💾 تسجيل الدفعة</button>
        </div>
      ) : null}

      <div className="vex-card" style={{ marginBottom: 16, display: 'flex', gap: 16, flexWrap: 'wrap' }}>
        <label className="vex-label" style={{ minWidth: 260 }}>العميل<EntityPicker value={filterCustomer} onChange={setFilterCustomer} search={searchCustomers} placeholder="كل العملاء" /></label>
        <label className="vex-label">الطريقة
          <select value={filterMethod} onChange={(e) => setFilterMethod(e.target.value)} className="vex-select"><option value="">الكل</option>{METHODS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}</select>
        </label>
      </div>

      <div className="vex-card vex-card--no-pad" style={{ opacity: list.loading ? 0.6 : 1 }}>
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead><tr><th>رقم السند</th><th>العميل</th><th>التاريخ</th><th>الطريقة</th><th>المبلغ</th><th>غير الموزّع</th><th>الحالة</th><th /></tr></thead>
            <tbody>
              {list.items.length === 0 ? (
                <tr><td colSpan={8} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>لا توجد دفعات</td></tr>
              ) : list.items.map((p) => (
                <tr key={p.id} style={p.isReversed ? { opacity: 0.55 } : undefined}>
                  <td style={{ fontWeight: 600, color: 'var(--clr-primary)' }}>{p.paymentNumber}</td>
                  <td>{p.customerName}</td>
                  <td style={{ color: 'var(--txt-secondary)' }}>{p.paymentDate}</td>
                  <td>{methodLabel(p.paymentMethod)}</td>
                  <td style={{ fontWeight: 700 }}>{p.amountUsd > 0 ? `${money(p.amountUsd)} $` : `${money(p.amountSyp)} ل.س`}</td>
                  <td>{p.unallocatedUsd > 0 ? `${money(p.unallocatedUsd)} $` : p.unallocatedSyp > 0 ? `${money(p.unallocatedSyp)} ل.س` : '—'}</td>
                  <td>{p.isReversed ? <span className="badge badge--danger">معكوسة</span> : <span className="badge badge--success">فعّالة</span>}</td>
                  <td>{!p.isReversed ? <button type="button" className="btn-ghost" style={{ padding: '4px 12px', fontSize: 12 }} onClick={() => { setReverseId(p.id); setReverseReason(''); }}>↩ عكس</button> : null}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <Pagination page={list.page} pageSize={list.pageSize} totalCount={list.totalCount} onPageChange={list.setPage} onPageSizeChange={list.changePageSize} />
      </div>

      {reverseId ? (
        <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,.4)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 50 }}>
          <div className="vex-card" style={{ width: 420, maxWidth: '92vw' }}>
            <h2 className="vex-section-title">عكس الدفعة</h2>
            <p style={{ fontSize: 13, color: 'var(--txt-secondary)' }}>ستُعاد المبالغ الموزّعة إلى أرصدة الفواتير ويُلغى القيد في المحاسبة.</p>
            <label className="vex-label">السبب *<input value={reverseReason} onChange={(e) => setReverseReason(e.target.value)} className="vex-input" /></label>
            <div style={{ display: 'flex', gap: 10, marginTop: 14 }}>
              <button type="button" disabled={busy === reverseId} className="btn-danger" onClick={() => void reverse()}>تأكيد العكس</button>
              <button type="button" className="btn-ghost" onClick={() => setReverseId('')}>إلغاء</button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}

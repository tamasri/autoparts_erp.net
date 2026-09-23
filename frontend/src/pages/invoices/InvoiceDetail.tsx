import { useEffect, useMemo, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { invoicesApi, type InvoiceAmounts } from '../../api/endpoints/invoices';
import { unwrapNode } from '../../api/apiData';
import { toast, extractApiError } from '../../lib/toast';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';
import StatusBadge from '../../components/common/StatusBadge';

type InvoiceLine = {
  id: string;
  skuCode?: string;
  skuName?: string;
  quantity?: number;
  unitPriceSyp?: number;
  discountPct?: number;
  lineTotalSyp?: number;
};

type PaymentHistory = {
  id: string;
  paymentNumber?: string;
  amountSyp?: number;
  paymentDate?: string;
};

type InvoiceDetail = {
  id: string;
  invoiceNumber?: string;
  status?: string;
  customerName?: string;
  invoiceDate?: string;
  dueDate?: string;
  amounts?: InvoiceAmounts;
  totalSyp?: number;
  totalUsd?: number;
  totalSypInWords?: string;
  lines?: InvoiceLine[];
  payments?: PaymentHistory[];
};


function extractError(e: unknown, fallback: string): string {
  return extractApiError(e, fallback);
}

export default function InvoiceDetail(): JSX.Element {
  const { id } = useParams();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [invoice, setInvoice] = useState<InvoiceDetail | null>(null);
  const [busy, setBusy] = useState(false);
  const [discountMode, setDiscountMode] = useState<'pct' | 'usd'>('pct');
  const [discountValue, setDiscountValue] = useState('');

  async function load(): Promise<void> {
    if (!id) return;
    setLoading(true);
    setError('');
    try {
      const res = await invoicesApi.getInvoiceById(id);
      setInvoice(unwrapNode<InvoiceDetail>(res.data));
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر تحميل تفاصيل الفاتورة'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  async function confirmInvoice(): Promise<void> {
    if (!id) return;
    setBusy(true);
    try {
      await invoicesApi.confirm(id);
      toast.success('تم تأكيد الفاتورة');
      await load();
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر تأكيد الفاتورة'));
      setError(extractApiError(e, 'تعذر تأكيد الفاتورة'));
    } finally {
      setBusy(false);
    }
  }

  async function postInvoice(): Promise<void> {
    if (!id) return;
    setBusy(true);
    try {
      await invoicesApi.post(id);
      toast.success('تم ترحيل الفاتورة بنجاح');
      await load();
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر ترحيل الفاتورة'));
      setError(extractApiError(e, 'تعذر ترحيل الفاتورة'));
    } finally {
      setBusy(false);
    }
  }

  async function voidInvoice(): Promise<void> {
    if (!id) return;
    const reason = window.prompt('سبب الإلغاء:') ?? '';
    if (!reason.trim()) return;
    setBusy(true);
    try {
      await invoicesApi.void(id, reason.trim());
      toast.success('تم إلغاء الفاتورة');
      await load();
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر إلغاء الفاتورة'));
      setError(extractApiError(e, 'تعذر إلغاء الفاتورة'));
    } finally {
      setBusy(false);
    }
  }

  async function saveDiscount(): Promise<void> {
    if (!id) return;
    const v = Number(discountValue || 0);
    setBusy(true);
    try {
      await invoicesApi.setDiscount(id, discountMode === 'pct' ? { discountPct: v > 0 ? v : null } : { discountAmountUsd: v > 0 ? v : null });
      toast.success(v > 0 ? 'تم تطبيق خصم الفاتورة' : 'أُزيل خصم الفاتورة');
      setDiscountValue('');
      await load();
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر تطبيق الخصم'));
    } finally {
      setBusy(false);
    }
  }

  async function downloadPdf(): Promise<void> {
    if (!id) return;
    setBusy(true);
    try {
      const res = await invoicesApi.getPdf(id);
      const blob = new Blob([res.data as BlobPart], { type: 'application/pdf' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `invoice-${invoice?.invoiceNumber ?? id}.pdf`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر تنزيل ملف PDF'));
      setError(extractApiError(e, 'تعذر تنزيل ملف PDF'));
    } finally {
      setBusy(false);
    }
  }

  const status = (invoice?.status ?? '').toUpperCase();
  const lines = useMemo(() => invoice?.lines ?? [], [invoice?.lines]);
  const payments = useMemo(() => invoice?.payments ?? [], [invoice?.payments]);
  const amounts = invoice?.amounts;
  const n = (v?: number | null): string => Math.abs(Number(v ?? 0)).toLocaleString('en-US');

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>

      {/* Page Header */}
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">تفاصيل الفاتورة</h1>
          <div className="vex-page-header__breadcrumb">
            <Link to="/invoices" style={{ color: 'var(--clr-primary)', textDecoration: 'none' }}>الفواتير</Link>
            {' / '}
            {invoice?.invoiceNumber ?? id}
          </div>
        </div>

        {/* Action Buttons */}
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
          <button
            type="button"
            disabled={busy}
            onClick={() => void downloadPdf()}
            className="btn-secondary"
          >
            ⬇ تنزيل PDF
          </button>
          {status === 'DRAFT' ? (
            <button type="button" disabled={busy} onClick={() => void confirmInvoice()} className="btn-primary">
              ✓ تأكيد
            </button>
          ) : null}
          {status === 'CONFIRMED' ? (
            <button type="button" disabled={busy} onClick={() => void postInvoice()} className="btn-success">
              ✓ ترحيل
            </button>
          ) : null}
          {status !== 'VOID' && status !== 'POSTED' ? (
            <button type="button" disabled={busy} onClick={() => void voidInvoice()} className="btn-danger">
              ✕ إلغاء
            </button>
          ) : null}
        </div>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {/* ── A4 PAPER ── */}
      <div className="invoice-paper">

        {/* Header: Title + Logo */}
        <div className="invoice-paper__header">
          <div>
            <h2 style={{ fontSize: 36, fontWeight: 800, color: 'var(--txt-primary)', margin: 0, letterSpacing: '-0.5px' }}>
              فاتورة
            </h2>
            <div style={{ marginTop: 10, display: 'flex', flexDirection: 'column', gap: 4 }}>
              <div style={{ fontSize: 13, color: 'var(--txt-secondary)' }}>
                <span style={{ color: 'var(--txt-muted)', marginLeft: 6 }}>رقم الفاتورة:</span>
                <span style={{ fontWeight: 700, color: 'var(--txt-primary)' }}>{invoice?.invoiceNumber ?? invoice?.id}</span>
              </div>
              <div style={{ fontSize: 13, color: 'var(--txt-secondary)' }}>
                <span style={{ color: 'var(--txt-muted)', marginLeft: 6 }}>تاريخ الإصدار:</span>
                <span style={{ fontWeight: 600 }}>{invoice?.invoiceDate ?? '-'}</span>
              </div>
              <div style={{ fontSize: 13, color: 'var(--txt-secondary)' }}>
                <span style={{ color: 'var(--txt-muted)', marginLeft: 6 }}>تاريخ الاستحقاق:</span>
                <span style={{ fontWeight: 600 }}>{invoice?.dueDate ?? '-'}</span>
              </div>
              <div style={{ marginTop: 6 }}>
                <StatusBadge status={invoice?.status ?? 'UNKNOWN'} type="invoice" />
              </div>
            </div>
          </div>

          {/* Company Logo (right) */}
          <div style={{ textAlign: 'left' }}>
            <div style={{
              width: 64,
              height: 64,
              borderRadius: 16,
              background: 'linear-gradient(135deg, #5c54ff, #7b75ff)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontSize: 28,
              fontWeight: 800,
              color: '#fff',
              boxShadow: '0 8px 24px rgba(92,84,255,0.25)',
              marginBottom: 8,
              marginLeft: 'auto',
            }}>A</div>
            <div style={{ fontSize: 15, fontWeight: 700, color: 'var(--txt-primary)', textAlign: 'left' }}>AutoParts ERP</div>
            <div style={{ fontSize: 12, color: 'var(--txt-muted)', textAlign: 'left' }}>نظام إدارة قطع الغيار</div>
          </div>
        </div>

        {/* Parties: Company (right) / Customer (left) */}
        <div className="invoice-paper__parties">
          {/* Company (Sender) — right column in RTL */}
          <div>
            <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--clr-primary)', textTransform: 'uppercase', letterSpacing: '0.6px', marginBottom: 10 }}>
              من
            </div>
            <div style={{ fontSize: 14, fontWeight: 700, color: 'var(--txt-primary)', marginBottom: 4 }}>AutoParts ERP</div>
            <div style={{ fontSize: 13, color: 'var(--txt-secondary)', lineHeight: 1.7 }}>
              <div>نظام إدارة قطع الغيار</div>
              <div>admin@autoparts.local</div>
            </div>
          </div>

          {/* Customer (Recipient) — left column in RTL */}
          <div style={{ borderRight: '1px solid var(--clr-border)', paddingRight: 24 }}>
            <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--txt-muted)', textTransform: 'uppercase', letterSpacing: '0.6px', marginBottom: 10 }}>
              إلى
            </div>
            <div style={{ fontSize: 14, fontWeight: 700, color: 'var(--txt-primary)', marginBottom: 4 }}>
              {invoice?.customerName ?? 'غير محدد'}
            </div>
            <div style={{ fontSize: 13, color: 'var(--txt-secondary)', lineHeight: 1.7 }}>
              <div>الزبون</div>
            </div>
          </div>
        </div>

        {/* Items Table */}
        <div className="invoice-paper__table-wrap">
          <h3 className="vex-section-title">الأصناف والخدمات</h3>
          <table className="vex-table">
            <thead>
              <tr>
                <th>رمز SKU</th>
                <th>الاسم / الوصف</th>
                <th>الكمية</th>
                <th>سعر الوحدة (ل.س)</th>
                <th>الخصم %</th>
                <th>الإجمالي (ل.س)</th>
              </tr>
            </thead>
            <tbody>
              {lines.length === 0 ? (
                <tr>
                  <td colSpan={6} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '28px 0' }}>
                    لا توجد أسطر
                  </td>
                </tr>
              ) : (
                lines.map((line) => (
                  <tr key={line.id}>
                    <td>
                      <span style={{
                        background: 'var(--clr-primary-light)',
                        color: 'var(--clr-primary-dark)',
                        padding: '2px 8px',
                        borderRadius: 'var(--radius-sm)',
                        fontSize: 12,
                        fontWeight: 600,
                      }}>
                        {line.skuCode ?? '-'}
                      </span>
                    </td>
                    <td style={{ fontWeight: 500, color: 'var(--txt-primary)' }}>{line.skuName ?? '-'}</td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{Number(line.quantity ?? 0).toLocaleString('en-US')}</td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{Number(line.unitPriceSyp ?? 0).toLocaleString('en-US')}</td>
                    <td>
                      {Number(line.discountPct ?? 0) > 0 ? (
                        <span className="badge badge--warning">{Number(line.discountPct ?? 0)}%</span>
                      ) : (
                        <span style={{ color: 'var(--txt-muted)' }}>—</span>
                      )}
                    </td>
                    <td style={{ fontWeight: 700, color: 'var(--txt-primary)' }}>
                      {Number(line.lineTotalSyp ?? 0).toLocaleString('en-US')}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {/* Totals Block — bottom right */}
        <div className="invoice-paper__totals">
          <div className="invoice-paper__totals-block">
            <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 13, color: 'var(--txt-secondary)' }}>
                <span>الإجمالي الفرعي</span>
                <span style={{ fontWeight: 600, color: 'var(--txt-primary)' }}>
                  {n(amounts?.subtotalSyp)} ل.س
                </span>
              </div>
              {Number(amounts?.discountAmountUsd ?? 0) > 0 && (
                <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 13, color: 'var(--txt-secondary)' }}>
                  <span>خصم الفاتورة{amounts?.discountPct ? ` ${amounts.discountPct}%` : ''}</span>
                  <span style={{ fontWeight: 600, color: 'var(--clr-danger)' }}>
                    −{n(amounts?.discountAmountSyp)} ل.س <span style={{ fontSize: 11, color: 'var(--txt-muted)' }}>(${n(amounts?.discountAmountUsd)})</span>
                  </span>
                </div>
              )}
              {status === 'DRAFT' && (
                <div style={{ display: 'flex', gap: 6, alignItems: 'center', fontSize: 12 }}>
                  <select className="vex-select" style={{ width: 90 }} value={discountMode} onChange={(e) => setDiscountMode(e.target.value as 'pct' | 'usd')}>
                    <option value="pct">خصم %</option>
                    <option value="usd">خصم $</option>
                  </select>
                  <input className="vex-input" type="number" min={0} style={{ width: 90 }} placeholder="0" value={discountValue} onChange={(e) => setDiscountValue(e.target.value)} />
                  <button type="button" className="btn-secondary" disabled={busy} onClick={() => void saveDiscount()}>تطبيق</button>
                </div>
              )}
              {Number(amounts?.deliveryFeeSyp ?? 0) > 0 && (
                <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 13, color: 'var(--txt-secondary)' }}>
                  <span>رسوم التوصيل</span>
                  <span style={{ fontWeight: 600, color: 'var(--txt-primary)' }}>
                    {n(amounts?.deliveryFeeSyp)} ل.س
                  </span>
                </div>
              )}
              <hr className="vex-divider" style={{ margin: '4px 0' }} />
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <span style={{ fontSize: 14, fontWeight: 700, color: 'var(--txt-primary)' }}>الإجمالي</span>
                <div style={{ textAlign: 'left' }}>
                  <div style={{ fontSize: 22, fontWeight: 800, color: 'var(--clr-primary)' }}>
                    {Number(invoice?.totalSyp ?? 0).toLocaleString('en-US')}
                    <span style={{ fontSize: 13, fontWeight: 500, marginRight: 4, color: 'var(--txt-secondary)' }}>ل.س</span>
                  </div>
                  {Number(invoice?.totalUsd ?? 0) > 0 && (
                    <div style={{ fontSize: 12, color: 'var(--txt-muted)', textAlign: 'left' }}>
                      ≈ {Number(invoice?.totalUsd ?? 0).toLocaleString('en-US')} $
                    </div>
                  )}
                </div>
              </div>
              {invoice?.totalSypInWords && (
                <div style={{
                  fontSize: 12,
                  color: 'var(--txt-muted)',
                  fontStyle: 'italic',
                  borderTop: '1px dashed var(--clr-border)',
                  paddingTop: 8,
                  marginTop: 4,
                }}>
                  {invoice.totalSypInWords}
                </div>
              )}
            </div>
          </div>
        </div>

        {/* Payments History */}
        {payments.length > 0 && (
          <div className="invoice-paper__payments">
            <h3 className="vex-section-title">سجل الدفعات</h3>
            <table className="vex-table">
              <thead>
                <tr>
                  <th>رقم الدفعة</th>
                  <th>التاريخ</th>
                  <th>المبلغ (ل.س)</th>
                </tr>
              </thead>
              <tbody>
                {payments.map((payment) => (
                  <tr key={payment.id}>
                    <td style={{ fontWeight: 600, color: 'var(--clr-primary)' }}>
                      {payment.paymentNumber ?? payment.id.slice(0, 8)}
                    </td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{payment.paymentDate ?? '-'}</td>
                    <td style={{ fontWeight: 700 }}>{Number(payment.amountSyp ?? 0).toLocaleString('en-US')}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

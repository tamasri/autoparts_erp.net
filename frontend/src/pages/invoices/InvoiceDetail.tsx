import { useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { invoicesApi } from '../../api/endpoints/invoices';
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
  subtotalSyp?: number;
  discountAmountSyp?: number;
  deliveryFeeSyp?: number;
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

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      {error ? <ErrorBanner message={error} /> : null}
      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', padding: '12px', marginBottom: '12px' }}>
        <h2 style={{ marginTop: 0 }}>{invoice?.invoiceNumber ?? invoice?.id}</h2>
        <div style={{ display: 'flex', gap: '10px', alignItems: 'center', flexWrap: 'wrap' }}>
          <StatusBadge status={invoice?.status ?? 'UNKNOWN'} type="invoice" />
          <span>العميل: {invoice?.customerName ?? '-'}</span>
          <span>التاريخ: {invoice?.invoiceDate ?? '-'}</span>
          <span>الاستحقاق: {invoice?.dueDate ?? '-'}</span>
        </div>
        <div style={{ display: 'flex', gap: '8px', marginTop: '12px', flexWrap: 'wrap' }}>
          {status === 'DRAFT' ? (
            <button type="button" disabled={busy} onClick={() => void confirmInvoice()} style={actBtn('#1565c0')}>تأكيد</button>
          ) : null}
          {status === 'CONFIRMED' ? (
            <button type="button" disabled={busy} onClick={() => void postInvoice()} style={actBtn('#2e7d32')}>ترحيل</button>
          ) : null}
          {status !== 'VOID' && status !== 'POSTED' ? (
            <button type="button" disabled={busy} onClick={() => void voidInvoice()} style={actBtn('#c62828')}>إلغاء</button>
          ) : null}
          <button type="button" disabled={busy} onClick={() => void downloadPdf()} style={actBtn('#00796b')}>تنزيل PDF</button>
        </div>
      </div>

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto', marginBottom: '12px' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th>SKU</th>
              <th>الاسم</th>
              <th>الكمية</th>
              <th>سعر الوحدة</th>
              <th>الخصم%</th>
              <th>الإجمالي</th>
            </tr>
          </thead>
          <tbody>
            {lines.map((line) => (
              <tr key={line.id}>
                <td>{line.skuCode ?? '-'}</td>
                <td>{line.skuName ?? '-'}</td>
                <td>{Number(line.quantity ?? 0).toLocaleString('en-US')}</td>
                <td>{Number(line.unitPriceSyp ?? 0).toLocaleString('en-US')}</td>
                <td>{Number(line.discountPct ?? 0).toLocaleString('en-US')}</td>
                <td>{Number(line.lineTotalSyp ?? 0).toLocaleString('en-US')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', padding: '12px', marginBottom: '12px' }}>
        <div>الإجمالي الفرعي: {Number(invoice?.subtotalSyp ?? 0).toLocaleString('en-US')}</div>
        <div>الخصم: {Number(invoice?.discountAmountSyp ?? 0).toLocaleString('en-US')}</div>
        <div>التوصيل: {Number(invoice?.deliveryFeeSyp ?? 0).toLocaleString('en-US')}</div>
        <div>الإجمالي ل.س: {Number(invoice?.totalSyp ?? 0).toLocaleString('en-US')}</div>
        <div>الإجمالي $: {Number(invoice?.totalUsd ?? 0).toLocaleString('en-US')}</div>
        <div>كتابة: {invoice?.totalSypInWords ?? '-'}</div>
      </div>

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th>رقم الدفعة</th>
              <th>التاريخ</th>
              <th>المبلغ</th>
            </tr>
          </thead>
          <tbody>
            {payments.map((payment) => (
              <tr key={payment.id}>
                <td>{payment.paymentNumber ?? payment.id.slice(0, 8)}</td>
                <td>{payment.paymentDate ?? '-'}</td>
                <td>{Number(payment.amountSyp ?? 0).toLocaleString('en-US')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function actBtn(bg: string): React.CSSProperties {
  return { border: 'none', borderRadius: '8px', background: bg, color: '#fff', padding: '8px 14px', cursor: 'pointer', fontSize: '13px' };
}

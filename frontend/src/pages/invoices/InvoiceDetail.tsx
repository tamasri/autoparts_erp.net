import { useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { invoicesApi } from '../../api/endpoints/invoices';
import { unwrapNode } from '../../api/apiData';
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


export default function InvoiceDetail(): JSX.Element {
  const { id } = useParams();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [invoice, setInvoice] = useState<InvoiceDetail | null>(null);

  useEffect(() => {
    if (!id) return;
    let mounted = true;
    async function load(): Promise<void> {
      if (!id) return;
      setLoading(true);
      setError('');
      try {
        const res = await invoicesApi.getInvoiceById(id);
        if (mounted) setInvoice(unwrapNode<InvoiceDetail>(res.data));
      } catch (e: unknown) {
        if (!mounted) return;
        const msg = (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.detail
          ?? (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.message
          ?? 'تعذر تحميل تفاصيل الفاتورة';
        setError(msg);
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void load();
    return () => {
      mounted = false;
    };
  }, [id]);

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

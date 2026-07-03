import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { invoicesApi } from '../../api/endpoints/invoices';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';
import StatusBadge from '../../components/common/StatusBadge';

type Invoice = {
  id: string;
  invoiceNumber?: string;
  customerName?: string;
  invoiceDate?: string;
  dueDate?: string;
  totalSyp?: number;
  totalUsd?: number;
  balanceSyp?: number;
  status?: string;
};


export default function Invoices(): JSX.Element {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [status, setStatus] = useState<string>('ALL');
  const [items, setItems] = useState<Invoice[]>([]);

  useEffect(() => {
    let mounted = true;
    async function load(): Promise<void> {
      setLoading(true);
      setError('');
      try {
        const res = await invoicesApi.getInvoices({
          page: 1,
          pageSize: 50,
          status: status === 'ALL' ? undefined : status,
        });
        if (mounted) setItems(unwrapList<Invoice>(res.data));
      } catch (e: unknown) {
        if (!mounted) return;
        const msg = (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.detail
          ?? (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.message
          ?? 'تعذر تحميل الفواتير';
        setError(msg);
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void load();
    return () => {
      mounted = false;
    };
  }, [status]);

  const today = useMemo(() => new Date(), []);

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      {error ? <ErrorBanner message={error} /> : null}
      <div style={{ display: 'flex', gap: '8px', marginBottom: '12px', justifyContent: 'space-between' }}>
        <div style={{ display: 'flex', gap: '8px' }}>
        {['ALL', 'DRAFT', 'POSTED', 'VOID'].map((s) => (
          <button
            key={s}
            type="button"
            onClick={() => setStatus(s)}
            style={{
              border: 'none',
              borderRadius: '8px',
              background: status === s ? '#00796b' : '#eceff1',
              color: status === s ? '#fff' : '#37474f',
              padding: '8px 12px',
              cursor: 'pointer',
            }}
          >
            {s === 'ALL' ? 'الكل' : s === 'DRAFT' ? 'مسودة' : s === 'POSTED' ? 'مرحّلة' : 'ملغاة'}
          </button>
        ))}
        </div>
        <button
          type="button"
          onClick={() => navigate('/invoices/new')}
          style={{ border: 'none', borderRadius: '8px', background: '#004d40', color: '#fff', padding: '8px 12px', cursor: 'pointer' }}
        >
          + فاتورة جديدة
        </button>
      </div>

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th>رقم الفاتورة</th>
              <th>العميل</th>
              <th>التاريخ</th>
              <th>تاريخ الاستحقاق</th>
              <th>الإجمالي ل.س</th>
              <th>الإجمالي $</th>
              <th>المتبقي ل.س</th>
              <th>الحالة</th>
            </tr>
          </thead>
          <tbody>
            {items.map((invoice) => {
              const due = invoice.dueDate ? new Date(invoice.dueDate) : null;
              const overdue = Boolean(due && due < today && (invoice.status ?? '').toUpperCase() === 'POSTED');
              return (
                <tr key={invoice.id} onClick={() => navigate(`/invoices/${invoice.id}`)} style={{ cursor: 'pointer' }}>
                  <td>{invoice.invoiceNumber ?? invoice.id.slice(0, 8)}</td>
                  <td>{invoice.customerName ?? '-'}</td>
                  <td>{invoice.invoiceDate ?? '-'}</td>
                  <td style={{ color: overdue ? '#c62828' : undefined, fontWeight: overdue ? 700 : 400 }}>{invoice.dueDate ?? '-'}</td>
                  <td>{Number(invoice.totalSyp ?? 0).toLocaleString('en-US')}</td>
                  <td>{Number(invoice.totalUsd ?? 0).toLocaleString('en-US')}</td>
                  <td>{Number(invoice.balanceSyp ?? 0).toLocaleString('en-US')}</td>
                  <td><StatusBadge status={invoice.status ?? 'UNKNOWN'} type="invoice" /></td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}

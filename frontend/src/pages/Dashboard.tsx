import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { customersApi } from '../api/endpoints/customers';
import { invoicesApi } from '../api/endpoints/invoices';
import { inventoryApi } from '../api/endpoints/inventory';
import { unwrapList } from '../api/apiData';
import ErrorBanner from '../components/common/ErrorBanner';
import KpiCard from '../components/common/KpiCard';
import LoadingSpinner from '../components/common/LoadingSpinner';
import StatusBadge from '../components/common/StatusBadge';

type InvoiceRow = {
  id: string;
  invoiceNumber?: string;
  customerName?: string;
  invoiceDate?: string;
  totalSyp?: number;
  status?: string;
  balanceSyp?: number;
};

type CustomerRow = { id: string; isActive?: boolean; code?: string; name?: string };
type StockRow = { id: string; quantityOnHand?: number; totalStock?: number };

export default function Dashboard(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [invoices, setInvoices] = useState<InvoiceRow[]>([]);
  const [customers, setCustomers] = useState<CustomerRow[]>([]);
  const [stock, setStock] = useState<StockRow[]>([]);

  useEffect(() => {
    let mounted = true;
    async function load(): Promise<void> {
      setLoading(true);
      setError('');
      try {
        const [invRes, cusRes, stkRes] = await Promise.all([
          invoicesApi.getInvoices({ status: 'POSTED', page: 1, pageSize: 5 }),
          customersApi.getCustomers({ page: 1, pageSize: 100 }),
          inventoryApi.getStock({ page: 1, pageSize: 100 }),
        ]);
        if (!mounted) return;
        setInvoices(unwrapList<InvoiceRow>(invRes.data));
        setCustomers(unwrapList<CustomerRow>(cusRes.data));
        setStock(unwrapList<StockRow>(stkRes.data));
      } catch (e: unknown) {
        if (!mounted) return;
        const message = (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.detail
          ?? (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.message
          ?? 'تعذر تحميل بيانات لوحة التحكم';
        setError(message);
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void load();
    return () => {
      mounted = false;
    };
  }, []);

  const kpis = useMemo(() => {
    const postedCount = invoices.length;
    const activeCustomers = customers.filter((c) => c.isActive ?? true).length;
    const receivables = invoices.reduce((sum, i) => sum + Number(i.balanceSyp ?? 0), 0);
    const outOfStock = stock.filter((s) => Number(s.totalStock ?? s.quantityOnHand ?? 0) <= 0).length;
    return { postedCount, activeCustomers, receivables, outOfStock };
  }, [customers, invoices, stock]);

  if (loading) return <LoadingSpinner />;

  const byCustomer = invoices.reduce<Record<string, number>>((acc, invoice) => {
    const key = invoice.customerName ?? 'غير معروف';
    acc[key] = (acc[key] ?? 0) + 1;
    return acc;
  }, {});

  return (
    <div style={{ direction: 'rtl' }}>
      {error ? <ErrorBanner message={error} /> : null}
      <div style={{ display: 'grid', gap: '12px', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))' }}>
        <KpiCard title="إجمالي الفواتير" value={kpis.postedCount} icon="🧾" />
        <KpiCard title="العملاء النشطون" value={kpis.activeCustomers} icon="👥" />
        <KpiCard title="الذمم المدينة" value={kpis.receivables.toLocaleString('en-US')} unit="ل.س" icon="💰" />
        <KpiCard title="المخزون المنخفض" value={kpis.outOfStock} icon="⚠️" />
      </div>

      <div style={{ background: '#fff', marginTop: '14px', borderRadius: '12px', padding: '14px', boxShadow: '0 2px 8px #00000012' }}>
        <h3 style={{ marginTop: 0 }}>آخر الفواتير</h3>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th>رقم الفاتورة</th>
              <th>العميل</th>
              <th>التاريخ</th>
              <th>الإجمالي (ل.س)</th>
              <th>الحالة</th>
            </tr>
          </thead>
          <tbody>
            {invoices.map((invoice) => (
              <tr key={invoice.id}>
                <td>{invoice.invoiceNumber ?? invoice.id.slice(0, 8)}</td>
                <td>{invoice.customerName ?? '-'}</td>
                <td>{invoice.invoiceDate ?? '-'}</td>
                <td>{Number(invoice.totalSyp ?? 0).toLocaleString('en-US')}</td>
                <td><StatusBadge status={invoice.status ?? 'UNKNOWN'} type="invoice" /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(260px,1fr))', gap: '12px', marginTop: '14px' }}>
        <div style={{ background: '#fff', borderRadius: '12px', padding: '14px', boxShadow: '0 2px 8px #00000012' }}>
          <h3 style={{ marginTop: 0 }}>أفضل العملاء</h3>
          {Object.entries(byCustomer).map(([customer, count]) => (
            <div key={customer} style={{ marginBottom: '10px' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '13px' }}>
                <span>{customer}</span>
                <span>{count}</span>
              </div>
              <div style={{ background: '#e0f2f1', borderRadius: '8px', overflow: 'hidden', height: '8px' }}>
                <div style={{ width: `${Math.min(100, count * 25)}%`, background: '#00796b', height: '8px' }} />
              </div>
            </div>
          ))}
        </div>
        <div style={{ background: '#fff', borderRadius: '12px', padding: '14px', boxShadow: '0 2px 8px #00000012' }}>
          <h3 style={{ marginTop: 0 }}>إجراءات سريعة</h3>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
            <Link to="/invoices" style={{ textDecoration: 'none', background: '#00796b', color: '#fff', padding: '10px', borderRadius: '8px' }}>فاتورة جديدة</Link>
            <Link to="/invoices" style={{ textDecoration: 'none', background: '#2e7d32', color: '#fff', padding: '10px', borderRadius: '8px' }}>استلام دفعة</Link>
            <Link to="/customers" style={{ textDecoration: 'none', background: '#1565c0', color: '#fff', padding: '10px', borderRadius: '8px' }}>عميل جديد</Link>
          </div>
        </div>
      </div>
    </div>
  );
}

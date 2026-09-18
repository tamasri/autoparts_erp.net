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

  const maxCount = Math.max(...Object.values(byCustomer), 1);

  return (
    <div style={{ direction: 'rtl' }}>

      {/* Page Header */}
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">لوحة التحكم</h1>
          <div className="vex-page-header__breadcrumb">نظرة عامة على أداء النظام</div>
        </div>
        <Link to="/invoices/new" className="btn-primary">
          <span>＋</span>
          فاتورة جديدة
        </Link>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {/* KPI Grid */}
      <div style={{
        display: 'grid',
        gap: 16,
        gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
        marginBottom: 24,
      }}>
        <KpiCard title="إجمالي الفواتير" value={kpis.postedCount} icon="🧾" colorVariant="primary" />
        <KpiCard title="العملاء النشطون" value={kpis.activeCustomers} icon="👥" colorVariant="success" />
        <KpiCard title="الذمم المدينة" value={kpis.receivables.toLocaleString('en-US')} unit="ل.س" icon="💰" colorVariant="warning" />
        <KpiCard title="المخزون المنخفض" value={kpis.outOfStock} icon="⚠️" colorVariant="danger" />
      </div>

      {/* Recent Invoices */}
      <div className="vex-card vex-card--no-pad" style={{ marginBottom: 20 }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '18px 22px 14px' }}>
          <h2 className="vex-section-title" style={{ margin: 0 }}>آخر الفواتير</h2>
          <Link to="/invoices" style={{ fontSize: 13, color: 'var(--clr-primary)', fontWeight: 600, textDecoration: 'none' }}>
            عرض الكل ←
          </Link>
        </div>
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
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
              {invoices.length === 0 ? (
                <tr>
                  <td colSpan={5} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>
                    لا توجد فواتير حالياً
                  </td>
                </tr>
              ) : (
                invoices.map((invoice) => (
                  <tr key={invoice.id}>
                    <td>
                      <Link
                        to={`/invoices/${invoice.id}`}
                        style={{ color: 'var(--clr-primary)', fontWeight: 600, textDecoration: 'none' }}
                      >
                        {invoice.invoiceNumber ?? invoice.id.slice(0, 8)}
                      </Link>
                    </td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{invoice.customerName ?? '-'}</td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{invoice.invoiceDate ?? '-'}</td>
                    <td style={{ fontWeight: 600 }}>{Number(invoice.totalSyp ?? 0).toLocaleString('en-US')}</td>
                    <td><StatusBadge status={invoice.status ?? 'UNKNOWN'} type="invoice" /></td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Bottom Row: Top Customers + Quick Actions */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px,1fr))', gap: 20 }}>

        {/* Top Customers */}
        <div className="vex-card">
          <h2 className="vex-section-title">أفضل العملاء</h2>
          {Object.keys(byCustomer).length === 0 ? (
            <p style={{ color: 'var(--txt-muted)', fontSize: 13 }}>لا توجد بيانات</p>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
              {Object.entries(byCustomer).map(([customer, count]) => (
                <div key={customer}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 13, marginBottom: 6 }}>
                    <span style={{ fontWeight: 500, color: 'var(--txt-primary)' }}>{customer}</span>
                    <span style={{ color: 'var(--clr-primary)', fontWeight: 700 }}>{count}</span>
                  </div>
                  <div className="vex-progress-bar">
                    <div
                      className="vex-progress-bar__fill"
                      style={{ width: `${Math.min(100, (count / maxCount) * 100)}%` }}
                    />
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Quick Actions */}
        <div className="vex-card">
          <h2 className="vex-section-title">إجراءات سريعة</h2>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            <Link
              to="/invoices/new"
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 10,
                padding: '12px 16px',
                background: 'linear-gradient(135deg, var(--clr-primary), var(--clr-primary-mid))',
                color: '#fff',
                borderRadius: 'var(--radius-md)',
                textDecoration: 'none',
                fontSize: 14,
                fontWeight: 600,
                transition: 'opacity var(--transition-fast), transform var(--transition-fast)',
              }}
              onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.opacity = '0.9'; (e.currentTarget as HTMLElement).style.transform = 'translateX(-2px)'; }}
              onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.opacity = '1'; (e.currentTarget as HTMLElement).style.transform = 'none'; }}
            >
              <span style={{ fontSize: 18 }}>🧾</span>
              فاتورة جديدة
            </Link>
            <Link
              to="/invoices"
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 10,
                padding: '12px 16px',
                background: 'linear-gradient(135deg, #22c55e, #4ade80)',
                color: '#fff',
                borderRadius: 'var(--radius-md)',
                textDecoration: 'none',
                fontSize: 14,
                fontWeight: 600,
                transition: 'opacity var(--transition-fast), transform var(--transition-fast)',
              }}
              onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.opacity = '0.9'; (e.currentTarget as HTMLElement).style.transform = 'translateX(-2px)'; }}
              onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.opacity = '1'; (e.currentTarget as HTMLElement).style.transform = 'none'; }}
            >
              <span style={{ fontSize: 18 }}>💳</span>
              استلام دفعة
            </Link>
            <Link
              to="/customers"
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 10,
                padding: '12px 16px',
                background: 'linear-gradient(135deg, #3b82f6, #60a5fa)',
                color: '#fff',
                borderRadius: 'var(--radius-md)',
                textDecoration: 'none',
                fontSize: 14,
                fontWeight: 600,
                transition: 'opacity var(--transition-fast), transform var(--transition-fast)',
              }}
              onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.opacity = '0.9'; (e.currentTarget as HTMLElement).style.transform = 'translateX(-2px)'; }}
              onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.opacity = '1'; (e.currentTarget as HTMLElement).style.transform = 'none'; }}
            >
              <span style={{ fontSize: 18 }}>👤</span>
              عميل جديد
            </Link>
            <Link
              to="/inventory/receiving"
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 10,
                padding: '12px 16px',
                background: 'linear-gradient(135deg, #f59e0b, #fbbf24)',
                color: '#fff',
                borderRadius: 'var(--radius-md)',
                textDecoration: 'none',
                fontSize: 14,
                fontWeight: 600,
                transition: 'opacity var(--transition-fast), transform var(--transition-fast)',
              }}
              onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.opacity = '0.9'; (e.currentTarget as HTMLElement).style.transform = 'translateX(-2px)'; }}
              onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.opacity = '1'; (e.currentTarget as HTMLElement).style.transform = 'none'; }}
            >
              <span style={{ fontSize: 18 }}>📦</span>
              استلام بضاعة
            </Link>
          </div>
        </div>
      </div>
    </div>
  );
}

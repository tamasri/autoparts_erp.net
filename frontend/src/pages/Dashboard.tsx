import { Link } from 'react-router-dom';
import { useDashboardSummary } from '../hooks/useDashboardSummary';
import ErrorBanner from '../components/common/ErrorBanner';
import KpiCard from '../components/common/KpiCard';
import LoadingSpinner from '../components/common/LoadingSpinner';
import SalesChart from '../components/common/SalesChart';
import StatusBadge from '../components/common/StatusBadge';

const fmt = (v: number): string => Number(v ?? 0).toLocaleString('en-US');

const QUICK_ACTIONS = [
  { to: '/invoices/new', label: 'فاتورة جديدة', icon: '🧾', gradient: 'linear-gradient(135deg, var(--clr-primary), var(--clr-primary-mid))' },
  { to: '/invoices', label: 'الفواتير والمستحقات', icon: '💳', gradient: 'linear-gradient(135deg, #22c55e, #4ade80)' },
  { to: '/accounts', label: 'العملاء', icon: '👤', gradient: 'linear-gradient(135deg, #3b82f6, #60a5fa)' },
  { to: '/inventory/receiving', label: 'استلام بضاعة', icon: '📦', gradient: 'linear-gradient(135deg, #f59e0b, #fbbf24)' },
];

export default function Dashboard(): JSX.Element {
  const { data, loading, error } = useDashboardSummary('تعذر تحميل بيانات لوحة التحكم');

  if (loading) return <LoadingSpinner />;

  const maxTop = Math.max(...(data?.topCustomers.map((c) => c.totalUsd) ?? [0]), 1);

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">لوحة التحكم</h1>
          <div className="vex-page-header__breadcrumb">نظرة عامة على أداء النظام — الأرقام محسوبة على كامل البيانات</div>
        </div>
        <Link to="/invoices/new" className="btn-primary">
          <span>＋</span>
          فاتورة جديدة
        </Link>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {data ? (
        <>
          <div style={{ display: 'grid', gap: 16, gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', marginBottom: 24 }}>
            <KpiCard title="مبيعات الشهر" value={`$${fmt(data.salesMonthUsd)}`} icon="📈" colorVariant="primary" trend={`${fmt(data.salesMonthSyp)} ل.س`} />
            <KpiCard title="مبيعات اليوم" value={`$${fmt(data.salesTodayUsd)}`} icon="🗓️" colorVariant="success" trend={`${fmt(data.salesTodaySyp)} ل.س`} />
            <KpiCard title="الذمم المدينة" value={`$${fmt(data.receivablesUsd)}`} icon="💰" colorVariant="warning" trend={`${fmt(data.receivablesSyp)} ل.س`} />
            <KpiCard title="فواتير متأخرة" value={data.overdueInvoices} icon="⏰" colorVariant={data.overdueInvoices > 0 ? 'danger' : 'success'} trend={`${fmt(data.overdueSyp)} ل.س متأخرة`} />
            <KpiCard title="فواتير مرحّلة" value={data.postedInvoices} icon="🧾" colorVariant="primary" />
            <KpiCard title="العملاء النشطون" value={data.activeCustomers} icon="👥" colorVariant="success" />
            <KpiCard title="أصناف نافدة" value={data.skusOutOfStock} icon="📦" colorVariant={data.skusOutOfStock > 0 ? 'danger' : 'success'} trend={`${data.skusLowStock} تحت حد الطلب · ${data.skusInStock} متوفرة`} />
            <KpiCard title="تنبيهات المخزون" value={data.openAlerts} icon="🚨" colorVariant={data.openAlerts > 0 ? 'danger' : 'success'} trend="تنبيهات غير مغلقة" />
          </div>

          <div className="vex-card" style={{ marginBottom: 20 }}>
            <h2 className="vex-section-title">المبيعات اليومية — آخر 30 يوماً ($)</h2>
            <SalesChart days={data.salesByDay} />
          </div>

          <div className="vex-card vex-card--no-pad" style={{ marginBottom: 20 }}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '18px 22px 14px' }}>
              <h2 className="vex-section-title" style={{ margin: 0 }}>آخر الفواتير المرحّلة</h2>
              <Link to="/invoices" style={{ fontSize: 13, color: 'var(--clr-primary)', fontWeight: 600, textDecoration: 'none' }}>عرض الكل ←</Link>
            </div>
            <div style={{ overflowX: 'auto' }}>
              <table className="vex-table">
                <thead>
                  <tr><th>رقم الفاتورة</th><th>العميل</th><th>التاريخ</th><th>الإجمالي (ل.س)</th><th>الإجمالي ($)</th><th>الحالة</th></tr>
                </thead>
                <tbody>
                  {data.recentInvoices.length === 0 ? (
                    <tr><td colSpan={6} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>لا توجد فواتير حالياً</td></tr>
                  ) : data.recentInvoices.map((invoice) => (
                    <tr key={invoice.id}>
                      <td><Link to={`/invoices/${invoice.id}`} style={{ color: 'var(--clr-primary)', fontWeight: 600, textDecoration: 'none' }}>{invoice.invoiceNumber || invoice.id.slice(0, 8)}</Link></td>
                      <td style={{ color: 'var(--txt-secondary)' }}>{invoice.customerName}</td>
                      <td style={{ color: 'var(--txt-secondary)' }}>{invoice.invoiceDate}</td>
                      <td style={{ fontWeight: 600 }}>{fmt(invoice.totalSyp)}</td>
                      <td style={{ color: 'var(--txt-secondary)' }}>{fmt(invoice.totalUsd)}</td>
                      <td><StatusBadge status={invoice.status} type="invoice" /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px,1fr))', gap: 20 }}>
            <div className="vex-card">
              <h2 className="vex-section-title">أفضل العملاء هذا الشهر</h2>
              {data.topCustomers.length === 0 ? (
                <p style={{ color: 'var(--txt-muted)', fontSize: 13 }}>لا توجد مبيعات هذا الشهر</p>
              ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
                  {data.topCustomers.map((c) => (
                    <div key={c.customerId}>
                      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 13, marginBottom: 6 }}>
                        <span style={{ fontWeight: 500, color: 'var(--txt-primary)' }}>{c.customerName}</span>
                        <span style={{ color: 'var(--clr-primary)', fontWeight: 700 }}>${fmt(c.totalUsd)} · {c.invoiceCount}</span>
                      </div>
                      <div className="vex-progress-bar">
                        <div className="vex-progress-bar__fill" style={{ width: `${Math.min(100, (c.totalUsd / maxTop) * 100)}%` }} />
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>

            <div className="vex-card">
              <h2 className="vex-section-title">إجراءات سريعة</h2>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                {QUICK_ACTIONS.map((a) => (
                  <Link
                    key={a.to}
                    to={a.to}
                    style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '12px 16px', background: a.gradient, color: '#fff', borderRadius: 'var(--radius-md)', textDecoration: 'none', fontSize: 14, fontWeight: 600 }}
                  >
                    <span style={{ fontSize: 18 }}>{a.icon}</span>
                    {a.label}
                  </Link>
                ))}
              </div>
            </div>
          </div>
        </>
      ) : null}
    </div>
  );
}

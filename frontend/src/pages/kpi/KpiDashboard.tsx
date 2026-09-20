import { useDashboardSummary } from '../../hooks/useDashboardSummary';
import ErrorBanner from '../../components/common/ErrorBanner';
import KpiCard from '../../components/common/KpiCard';
import LoadingSpinner from '../../components/common/LoadingSpinner';

const fmt = (v: number): string => Number(v ?? 0).toLocaleString('en-US');

export default function KpiDashboard(): JSX.Element {
  const { data, loading, error } = useDashboardSummary('تعذر تحميل مؤشرات الأداء');

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">مؤشرات الأداء الرئيسية</h1>
          <div className="vex-page-header__breadcrumb">لمحة شاملة عن أداء المنشأة — محسوبة على كامل البيانات</div>
        </div>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {data ? (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 16 }}>
          <KpiCard title="الزبائن النشطون" value={data.activeCustomers} icon="👥" colorVariant="success" trend="إجمالي الزبائن المفعّلين" />
          <KpiCard title="فواتير مرحّلة" value={data.postedInvoices} icon="🧾" colorVariant="primary" trend="الفواتير ذات الحالة POSTED" />
          <KpiCard title="الذمم المدينة" value={`$${fmt(data.receivablesUsd)}`} icon="💰" colorVariant="warning" trend={`${fmt(data.receivablesSyp)} ل.س — مجموع الأرصدة المستحقة`} />
          <KpiCard title="مستحقات متأخرة" value={data.overdueInvoices} icon="⏰" colorVariant={data.overdueInvoices > 0 ? 'danger' : 'success'} trend={`${fmt(data.overdueSyp)} ل.س تجاوزت تاريخ الاستحقاق`} />
          <KpiCard title="أصناف متوفرة" value={data.skusInStock} icon="✅" colorVariant="success" trend="أصناف بمخزون > 0" />
          <KpiCard title="أصناف نافدة" value={data.skusOutOfStock} icon="📦" colorVariant={data.skusOutOfStock > 0 ? 'danger' : 'success'} trend="أصناف بمخزون = 0" />
          <KpiCard title="تحت حد إعادة الطلب" value={data.skusLowStock} icon="📉" colorVariant={data.skusLowStock > 0 ? 'warning' : 'success'} trend="متوفرة لكن أقل من حد الطلب" />
          <KpiCard title="تنبيهات المخزون" value={data.openAlerts} icon="🚨" colorVariant={data.openAlerts > 0 ? 'danger' : 'success'} trend="تنبيهات غير مغلقة" />
        </div>
      ) : null}
    </div>
  );
}

import { Alert, Box, CircularProgress } from '@mui/material';
import { useDashboardSummary } from '../../hooks/useDashboardSummary';
import PageHeader from '../../components/ui/PageHeader';
import KpiTile from '../../components/ui/KpiTile';

const fmt = (v: number): string => Number(v ?? 0).toLocaleString('en-US');

export default function KpiDashboard(): JSX.Element {
  const { data, loading, error } = useDashboardSummary('تعذر تحميل مؤشرات الأداء');
  if (loading) return <Box sx={{ display: 'grid', placeItems: 'center', minHeight: '50vh' }}><CircularProgress /></Box>;

  return (
    <>
      <PageHeader title="مؤشرات الأداء الرئيسية" subtitle="لمحة شاملة عن أداء المنشأة — محسوبة على كامل البيانات" />
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      {data ? (
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 2 }}>
          <KpiTile icon="👥" title="الزبائن النشطون" value={data.activeCustomers} tone="success" hint="إجمالي الزبائن المفعّلين" />
          <KpiTile icon="🧾" title="فواتير مرحّلة" value={data.postedInvoices} hint="الفواتير المرحّلة" />
          <KpiTile icon="💰" title="الذمم المدينة" value={`$${fmt(data.receivablesUsd)}`} tone="warning" hint={`${fmt(data.receivablesSyp)} ل.س — مجموع الأرصدة المستحقة`} />
          <KpiTile icon="⏰" title="مستحقات متأخرة" value={data.overdueInvoices} tone={data.overdueInvoices > 0 ? 'error' : 'success'} hint={`${fmt(data.overdueSyp)} ل.س تجاوزت تاريخ الاستحقاق`} />
          <KpiTile icon="✅" title="أصناف متوفرة" value={data.skusInStock} tone="success" hint="أصناف بمخزون > 0" />
          <KpiTile icon="📦" title="أصناف نافدة" value={data.skusOutOfStock} tone={data.skusOutOfStock > 0 ? 'error' : 'success'} hint="أصناف بمخزون = 0" />
          <KpiTile icon="📉" title="تحت حد إعادة الطلب" value={data.skusLowStock} tone={data.skusLowStock > 0 ? 'warning' : 'success'} hint="متوفرة لكن أقل من حد الطلب" />
          <KpiTile icon="🚨" title="تنبيهات المخزون" value={data.openAlerts} tone={data.openAlerts > 0 ? 'error' : 'success'} hint="تنبيهات غير مغلقة" />
        </Box>
      ) : null}
    </>
  );
}

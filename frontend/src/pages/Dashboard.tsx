import { Link as RouterLink } from 'react-router-dom';
import { Alert, Box, Button, Card, CardContent, CircularProgress, LinearProgress, Stack, Typography } from '@mui/material';
import { useDashboardSummary } from '../hooks/useDashboardSummary';
import PageHeader from '../components/ui/PageHeader';
import KpiTile from '../components/ui/KpiTile';
import DataTable, { type Column } from '../components/ui/DataTable';
import StatusChip from '../components/ui/StatusChip';
import SalesChart from '../components/common/SalesChart';

const fmt = (v: number): string => Number(v ?? 0).toLocaleString('en-US');

const QUICK_ACTIONS = [
  { to: '/invoices/new', label: 'فاتورة مبيعات جديدة', icon: '🧾' },
  { to: '/payments', label: 'سند قبض', icon: '💳' },
  { to: '/purchasing', label: 'فاتورة شراء', icon: '🛒' },
  { to: '/inventory/receiving', label: 'استلام بضاعة', icon: '📦' },
];

type RecentInvoice = { id: string; invoiceNumber?: string; customerName: string; invoiceDate: string; totalSyp: number; totalUsd: number; status: string };

const RECENT_COLUMNS: Column<RecentInvoice>[] = [
  { header: 'رقم الفاتورة', render: (i) => <Button size="small" component={RouterLink} to={`/invoices/${i.id}`}>{i.invoiceNumber || i.id.slice(0, 8)}</Button> },
  { header: 'الزبون', render: (i) => i.customerName },
  { header: 'التاريخ', render: (i) => i.invoiceDate },
  { header: 'الإجمالي (ل.س)', numeric: true, render: (i) => fmt(i.totalSyp) },
  { header: 'الإجمالي ($)', numeric: true, render: (i) => fmt(i.totalUsd) },
  { header: 'الحالة', render: (i) => <StatusChip status={i.status} /> },
];

export default function Dashboard(): JSX.Element {
  const { data, loading, error } = useDashboardSummary('تعذر تحميل بيانات لوحة التحكم');

  if (loading) return <Box sx={{ display: 'grid', placeItems: 'center', minHeight: '50vh' }}><CircularProgress /></Box>;
  const maxTop = Math.max(...(data?.topCustomers.map((c) => c.totalUsd) ?? [0]), 1);

  return (
    <>
      <PageHeader title="لوحة التحكم" subtitle="نظرة عامة على أداء النظام — الأرقام محسوبة على كامل البيانات"
        actions={<Button variant="contained" size="small" component={RouterLink} to="/invoices/new">＋ فاتورة جديدة</Button>} />
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}

      {data ? (
        <>
          <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', mb: 3 }}>
            <KpiTile icon="📈" title="مبيعات الشهر" value={`$${fmt(data.salesMonthUsd)}`} hint={`${fmt(data.salesMonthSyp)} ل.س`} />
            <KpiTile icon="🗓️" title="مبيعات اليوم" value={`$${fmt(data.salesTodayUsd)}`} hint={`${fmt(data.salesTodaySyp)} ل.س`} tone="success" />
            <KpiTile icon="💰" title="الذمم المدينة" value={`$${fmt(data.receivablesUsd)}`} hint={`${fmt(data.receivablesSyp)} ل.س`} tone="warning" />
            <KpiTile icon="⏰" title="فواتير متأخرة" value={data.overdueInvoices} hint={`${fmt(data.overdueSyp)} ل.س متأخرة`} tone={data.overdueInvoices > 0 ? 'error' : 'success'} />
            <KpiTile icon="🧾" title="فواتير مرحّلة" value={data.postedInvoices} />
            <KpiTile icon="👥" title="الزبائن النشطون" value={data.activeCustomers} tone="success" />
            <KpiTile icon="📦" title="أصناف نافدة" value={data.skusOutOfStock} hint={`${data.skusLowStock} تحت حد الطلب · ${data.skusInStock} متوفرة`} tone={data.skusOutOfStock > 0 ? 'error' : 'success'} />
            <KpiTile icon="🚨" title="تنبيهات المخزون" value={data.openAlerts} hint="تنبيهات غير مغلقة" tone={data.openAlerts > 0 ? 'error' : 'success'} />
          </Box>

          <Card variant="outlined" sx={{ borderRadius: 3, mb: 3 }}>
            <CardContent><Typography variant="h6" fontWeight={700} sx={{ mb: 1 }}>المبيعات اليومية — آخر 30 يوماً ($)</Typography><SalesChart days={data.salesByDay} /></CardContent>
          </Card>

          <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1 }}>
            <Typography variant="h6" fontWeight={700}>آخر الفواتير المرحّلة</Typography>
            <Button size="small" component={RouterLink} to="/invoices">عرض الكل ←</Button>
          </Stack>
          <Box sx={{ mb: 3 }}><DataTable columns={RECENT_COLUMNS} rows={data.recentInvoices as RecentInvoice[]} getKey={(i) => i.id} empty="لا توجد فواتير حالياً" /></Box>

          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))', gap: 3 }}>
            <Card variant="outlined" sx={{ borderRadius: 3 }}>
              <CardContent>
                <Typography variant="h6" fontWeight={700} sx={{ mb: 2 }}>أفضل الزبائن هذا الشهر</Typography>
                {data.topCustomers.length === 0 ? <Typography color="text.secondary">لا توجد مبيعات هذا الشهر</Typography> : (
                  <Stack spacing={2}>
                    {data.topCustomers.map((c) => (
                      <Box key={c.customerId}>
                        <Stack direction="row" justifyContent="space-between"><Typography variant="body2">{c.customerName}</Typography><Typography variant="body2" color="primary" fontWeight={700}>${fmt(c.totalUsd)} · {c.invoiceCount}</Typography></Stack>
                        <LinearProgress variant="determinate" value={Math.min(100, (c.totalUsd / maxTop) * 100)} sx={{ height: 8, borderRadius: 4, mt: 0.5 }} />
                      </Box>
                    ))}
                  </Stack>
                )}
              </CardContent>
            </Card>
            <Card variant="outlined" sx={{ borderRadius: 3 }}>
              <CardContent>
                <Typography variant="h6" fontWeight={700} sx={{ mb: 2 }}>إجراءات سريعة</Typography>
                <Stack spacing={1}>
                  {QUICK_ACTIONS.map((a) => <Button key={a.to} component={RouterLink} to={a.to} variant="outlined" sx={{ justifyContent: 'flex-start' }}>{a.icon}&nbsp;&nbsp;{a.label}</Button>)}
                </Stack>
              </CardContent>
            </Card>
          </Box>
        </>
      ) : null}
    </>
  );
}

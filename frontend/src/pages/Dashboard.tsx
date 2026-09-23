import { Link as RouterLink } from 'react-router-dom';
import { Alert, Box, Button, Card, CardContent, Chip, Stack, Typography } from '@mui/material';
import { useDashboardSummary } from '../hooks/useDashboardSummary';
import PageHeader from '../components/ui/PageHeader';
import DataTable, { type Column } from '../components/ui/DataTable';
import StatusChip from '../components/ui/StatusChip';
import Money from '../components/ui/Money';
import BusinessKpis from '../features/dashboard/BusinessKpis';
import type { RecentInvoice } from '../api/endpoints/dashboard';

const QUICK_ACTIONS = [
  { to: '/invoices/new', label: 'فاتورة مبيعات جديدة', icon: '🧾' },
  { to: '/payments', label: 'سند قبض', icon: '💳' },
  { to: '/purchasing', label: 'فاتورة شراء', icon: '🛒' },
  { to: '/inventory/receiving', label: 'استلام بضاعة', icon: '📦' },
];

const RECENT_COLUMNS: Column<RecentInvoice>[] = [
  { header: 'رقم الفاتورة', render: (i) => <Button size="small" component={RouterLink} to={`/invoices/${i.id}`}>{i.invoiceNumber || i.id.slice(0, 8)}</Button> },
  { header: 'الزبون', render: (i) => i.customerName },
  { header: 'التاريخ', render: (i) => i.invoiceDate },
  { header: 'الإجمالي', numeric: true, render: (i) => <Money usd={i.totalUsd} syp={i.totalSyp} inline /> },
  { header: 'الحالة', render: (i) => <StatusChip status={i.status} /> },
];

/** The home screen: the business KPIs with their filters, then today's work (latest invoices, stock alerts, quick actions). */
export default function Dashboard(): JSX.Element {
  const { data, error } = useDashboardSummary('تعذر تحميل آخر الفواتير');

  return (
    <>
      <PageHeader title="لوحة التحكم" subtitle="مؤشرات الأداء محسوبة على الخادم لكامل البيانات — اختر الفترة والفلاتر"
        actions={<Button variant="contained" size="small" component={RouterLink} to="/invoices/new">＋ فاتورة جديدة</Button>} />
      <BusinessKpis />

      {error ? <Alert severity="error" sx={{ mt: 2 }}>{error}</Alert> : null}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '2fr 1fr' }, gap: 3, mt: 3 }}>
        <Box>
          <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1 }}>
            <Typography variant="h6" fontWeight={700}>آخر الفواتير المرحّلة</Typography>
            <Button size="small" component={RouterLink} to="/invoices">عرض الكل ←</Button>
          </Stack>
          <DataTable columns={RECENT_COLUMNS} rows={data?.recentInvoices ?? []} getKey={(i) => i.id} empty="لا توجد فواتير حالياً" />
        </Box>
        <Card variant="outlined" sx={{ borderRadius: 3 }}>
          <CardContent>
            <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 2 }}>
              <Typography variant="h6" fontWeight={700}>إجراءات سريعة</Typography>
              {data ? (
                <Chip component={RouterLink} to="/inventory/alerts" clickable size="small" color={data.openAlerts > 0 ? 'error' : 'success'} label={`تنبيهات المخزون: ${data.openAlerts}`} />
              ) : null}
            </Stack>
            <Stack spacing={1}>
              {QUICK_ACTIONS.map((a) => <Button key={a.to} component={RouterLink} to={a.to} variant="outlined" sx={{ justifyContent: 'flex-start' }}>{a.icon}&nbsp;&nbsp;{a.label}</Button>)}
            </Stack>
          </CardContent>
        </Card>
      </Box>
    </>
  );
}

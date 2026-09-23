/** One rep: headline figures for the period, a twelve-month trend, their customers (with what each owes) and their invoices. */
import { useState } from 'react';
import { Link as RouterLink, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Link, Paper, Stack, TextField, Typography } from '@mui/material';
import { BarChart } from '@mui/x-charts/BarChart';
import { salesRepsApi, SALES_REPS, type SalesRepDetail as Detail } from '../../api/endpoints/salesReps';
import { unwrapNode } from '../../api/apiData';
import { useLoad } from '../../hooks/useLoad';
import { useCan } from '../../hooks/useCan';
import { money, today } from '../../lib/money';
import { num, ymd, type ExportDocument } from '../../lib/exportClient';
import PageHeader from '../../components/ui/PageHeader';
import DataTable from '../../components/ui/DataTable';
import KpiTile from '../../components/ui/KpiTile';
import ExportMenu from '../../components/ui/ExportMenu';
import SalesRepDialog from '../../features/salesReps/SalesRepDialog';
import AssignCustomersDialog from '../../features/salesReps/AssignCustomersDialog';
import { TargetBar } from './SalesReps';
import Money from '../../components/ui/Money';

const MONTHS = ['كانون الثاني', 'شباط', 'آذار', 'نيسان', 'أيار', 'حزيران', 'تموز', 'آب', 'أيلول', 'تشرين الأول', 'تشرين الثاني', 'كانون الأول'];

export default function SalesRepDetail(): JSX.Element {
  const { userId = '' } = useParams();
  const [params] = useSearchParams();
  const canManage = useCan(SALES_REPS.manage);
  const [from, setFrom] = useState(params.get('from') ?? `${today().slice(0, 8)}01`);
  const [to, setTo] = useState(params.get('to') ?? today());
  const [editing, setEditing] = useState(false);
  const [assigning, setAssigning] = useState(false);

  const { data, loading, error, reload } = useLoad(
    async () => unwrapNode<Detail>((await salesRepsApi.detail(userId, { from, to })).data) as Detail,
    [userId, from, to], 'تعذر تحميل بيانات المندوب');
  const rep = data?.rep;

  const buildExport = async (): Promise<ExportDocument> => ({
    title: `كشف المندوب ${rep?.fullName ?? ''}`, subtitle: `${from} — ${to}`, fileName: `sales-rep-${rep?.userName ?? userId}-${from}-${to}`,
    fields: rep ? [
      { label: 'صافي المبيعات ($)', value: money(rep.netSalesUsd) }, { label: 'المحصّل ($)', value: money(rep.collectedUsd) },
      { label: 'ذمم الزبائن ($)', value: money(rep.outstandingUsd) }, { label: `العمولة ${rep.commissionPct}% ($)`, value: money(rep.commissionUsd) },
      { label: 'الهدف ($)', value: money(rep.targetUsd) },
    ] : [],
    tables: [
      {
        title: 'الفواتير', columns: ['الرقم', 'النوع', 'التاريخ', 'الزبون', 'الإجمالي ($)', 'المتبقي ($)'],
        rows: (data?.invoices ?? []).map((i) => [i.invoiceNumber ?? '', i.type === 'RETURN' ? 'مرتجع' : 'بيع', ymd(i.invoiceDate), i.customerName, num(i.totalUsd), num(i.balanceUsd)]),
        numericColumns: [4, 5],
      },
      {
        title: 'الزبائن', columns: ['الكود', 'الزبون', 'الهاتف', 'الذمة ($)', 'آخر فاتورة'],
        rows: (data?.customers ?? []).map((c) => [c.code, c.name, c.phone ?? '', num(c.outstandingUsd), ymd(c.lastInvoiceDate)]),
        numericColumns: [3],
      },
    ],
  });

  return (
    <Box>
      <PageHeader
        title={rep ? rep.fullName : 'المندوب'}
        subtitle={rep ? `${rep.userName} · عمولة ${rep.commissionPct}% · هدف شهري $${money(rep.monthlyTargetUsd)}${rep.isActive ? '' : ' · موقوف'}` : undefined}
        crumbs={[{ label: 'مندوبو المبيعات', to: '/sales-reps' }, { label: rep?.fullName ?? '' }]}
        actions={canManage && rep ? (
          <Stack direction="row" spacing={1}>
            <Button variant="outlined" onClick={() => setAssigning(true)} disabled={!rep.isActive}>إسناد زبائن</Button>
            <Button variant="contained" onClick={() => setEditing(true)}>تعديل</Button>
          </Stack>
        ) : undefined}
      />
      <Stack direction="row" gap={1.5} alignItems="center" flexWrap="wrap" sx={{ mb: 2 }}>
        <TextField size="small" type="date" label="من" value={from} onChange={(e) => setFrom(e.target.value)} InputLabelProps={{ shrink: true }} />
        <TextField size="small" type="date" label="إلى" value={to} onChange={(e) => setTo(e.target.value)} InputLabelProps={{ shrink: true }} />
        <Box sx={{ mr: 'auto' }} />
        <ExportMenu build={buildExport} disabled={!rep} />
      </Stack>
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      {rep ? (
        <>
          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(190px, 1fr))', gap: 2, mb: 2 }}>
            <KpiTile title="صافي المبيعات" value={<Money usd={rep.netSalesUsd} variant="h5" fontWeight={800} />} hint={`${rep.invoiceCount} فاتورة`} />
            <KpiTile title="المحصّل" value={<Money usd={rep.collectedUsd} variant="h5" fontWeight={800} />} tone="success" />
            <KpiTile title="ذمم زبائنه" value={<Money usd={rep.outstandingUsd} variant="h5" fontWeight={800} />} tone="warning" hint={`${rep.customerCount} زبون`} />
            <KpiTile title="العمولة" value={<Money usd={rep.commissionUsd} variant="h5" fontWeight={800} />} hint={`${rep.commissionPct}% من الصافي بدون التوصيل`} />
            <Paper variant="outlined" sx={{ p: 2, borderRadius: 3 }}>
              <Typography variant="caption" color="text.secondary">الهدف للفترة</Typography>
              <Box sx={{ mt: 1 }}><TargetBar value={rep.netSalesUsd} target={rep.targetUsd} /></Box>
            </Paper>
          </Box>

          <Paper variant="outlined" sx={{ p: 2, borderRadius: 3, mb: 2 }}>
            <Typography fontWeight={700} sx={{ mb: 1 }}>آخر 12 شهراً ($)</Typography>
            <BarChart
              height={260}
              xAxis={[{ scaleType: 'band', data: (data?.months ?? []).map((m) => `${MONTHS[m.month - 1]} ${String(m.year).slice(2)}`) }]}
              series={[
                { data: (data?.months ?? []).map((m) => m.netSalesUsd), label: 'صافي المبيعات' },
                { data: (data?.months ?? []).map((m) => m.collectedUsd), label: 'المحصّل' },
              ]}
              margin={{ top: 40, bottom: 30, left: 60, right: 10 }}
            />
          </Paper>

          <Typography variant="h6" fontWeight={700} sx={{ mb: 1 }}>الزبائن ({data?.customers.length ?? 0})</Typography>
          <Box sx={{ mb: 3 }}>
            <DataTable
              rows={data?.customers ?? []} getKey={(c) => c.id} loading={loading} empty="لا يوجد زبائن مسندون"
              columns={[
                { header: 'الكود', render: (c) => c.code, nowrap: true },
                { header: 'الزبون', render: (c) => <Link component={RouterLink} to={`/customers/${c.id}`}>{c.name}</Link> },
                { header: 'الهاتف', render: (c) => c.phone ?? '' },
                { header: 'الذمة', render: (c) => <Money usd={c.outstandingUsd} />, numeric: true },
                { header: 'آخر فاتورة', render: (c) => ymd(c.lastInvoiceDate) || '—', nowrap: true },
              ]}
            />
          </Box>

          <Typography variant="h6" fontWeight={700} sx={{ mb: 1 }}>فواتير الفترة ({data?.invoices.length ?? 0})</Typography>
          <DataTable
            rows={data?.invoices ?? []} getKey={(i) => i.id} loading={loading} empty="لا توجد فواتير مرحّلة في الفترة"
            columns={[
              { header: 'الرقم', render: (i) => <Link component={RouterLink} to={`/invoices/${i.id}`}>{i.invoiceNumber ?? i.id.slice(0, 8)}</Link>, nowrap: true },
              { header: 'النوع', render: (i) => (i.type === 'RETURN' ? 'مرتجع' : 'بيع') },
              { header: 'التاريخ', render: (i) => ymd(i.invoiceDate), nowrap: true },
              { header: 'الزبون', render: (i) => i.customerName },
              { header: 'الإجمالي', render: (i) => <Money usd={i.totalUsd} />, numeric: true },
              { header: 'المتبقي', render: (i) => <Money usd={i.balanceUsd} />, numeric: true },
            ]}
          />
          <SalesRepDialog open={editing} rep={rep} onClose={() => setEditing(false)} onSaved={reload} />
          <AssignCustomersDialog open={assigning} repId={rep.userId} repName={rep.fullName} onClose={() => setAssigning(false)} onSaved={reload} />
        </>
      ) : null}
    </Box>
  );
}

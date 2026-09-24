/**
 * Every rep's KPIs side by side for a period: how much of the target each reached, net sales against gross profit and commission,
 * average invoice, returns rate, collection rate and receivables — the team totals on top. Commission is a percentage of the gross
 * profit of the rep's posted invoices; the target is in net sales.
 */
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Alert, Box, Button, Chip, Paper, Stack, TextField, Typography } from '@mui/material';
import { BarChart } from '@mui/x-charts/BarChart';
import { salesRepsApi, type SalesRep } from '../../api/endpoints/salesReps';
import { unwrapNode } from '../../api/apiData';
import { useLoad } from '../../hooks/useLoad';
import { today } from '../../lib/money';
import { num, type ExportDocument } from '../../lib/exportClient';
import PageHeader from '../../components/ui/PageHeader';
import DataTable, { type Column } from '../../components/ui/DataTable';
import KpiTile from '../../components/ui/KpiTile';
import ExportMenu from '../../components/ui/ExportMenu';
import Money from '../../components/ui/Money';
import TargetGauge from '../../features/salesReps/TargetGauge';
import { TargetBar } from './SalesReps';

const monthStart = (): string => `${today().slice(0, 8)}01`;
const pct = (v: number | null): string => (v === null ? '—' : `${v}%`);

export default function SalesRepKpis(): JSX.Element {
  const navigate = useNavigate();
  const [from, setFrom] = useState(monthStart());
  const [to, setTo] = useState(today());

  const { data, loading, error } = useLoad(
    async () => unwrapNode<SalesRep[]>((await salesRepsApi.list({ from, to, includeInactive: false })).data) ?? [],
    [from, to], 'تعذر تحميل مؤشرات المندوبين');
  const reps = data ?? [];
  const sum = (f: (r: SalesRep) => number): number => reps.reduce((a, r) => a + f(r), 0);
  const team = {
    net: sum((r) => r.netSalesUsd), target: sum((r) => r.targetUsd), profit: sum((r) => r.grossProfitUsd),
    commission: sum((r) => r.commissionUsd), collected: sum((r) => r.collectedUsd), outstanding: sum((r) => r.outstandingUsd),
  };
  const teamAchievement = team.target > 0 ? Math.round((team.net / team.target) * 1000) / 10 : null;
  const ranked = [...reps].sort((a, b) => (b.achievementPct ?? -1) - (a.achievementPct ?? -1));

  const columns: Column<SalesRep>[] = [
    {
      header: 'المندوب',
      render: (r) => <Button size="small" onClick={() => navigate(`/sales-reps/${r.userId}?from=${from}&to=${to}`)} sx={{ fontWeight: 700 }}>{r.fullName}</Button>,
    },
    { header: 'الإنجاز', render: (r) => <TargetBar rep={r} /> },
    { header: 'صافي المبيعات', render: (r) => <Money usd={r.netSalesUsd} fontWeight={700} />, numeric: true, nowrap: true },
    { header: 'الربح الإجمالي', render: (r) => <Money usd={r.grossProfitUsd} />, numeric: true, nowrap: true },
    { header: 'الهامش', render: (r) => pct(r.grossMarginPct), numeric: true },
    { header: 'العمولة', render: (r) => <><Money usd={r.commissionUsd} /> <Typography component="span" variant="caption" color="text.secondary">({r.commissionPct}%)</Typography></>, numeric: true, nowrap: true },
    { header: 'الفواتير', render: (r) => r.invoiceCount, numeric: true },
    { header: 'متوسط الفاتورة', render: (r) => <Money usd={r.averageInvoiceUsd} />, numeric: true, nowrap: true },
    {
      header: 'نسبة المرتجعات', numeric: true,
      render: (r) => (r.returnRatePct === null ? '—' : <Chip size="small" variant="outlined" color={r.returnRatePct > 10 ? 'error' : 'default'} label={pct(r.returnRatePct)} />),
    },
    { header: 'نسبة التحصيل', render: (r) => pct(r.collectionRatePct), numeric: true },
    { header: 'ذمم الزبائن', render: (r) => <Money usd={r.outstandingUsd} />, numeric: true, nowrap: true },
    { header: 'الزبائن', render: (r) => r.customerCount, numeric: true },
  ];

  const buildExport = async (): Promise<ExportDocument> => ({
    title: 'مؤشرات أداء مندوبي المبيعات', subtitle: `${from} — ${to}`, fileName: `sales-rep-kpis-${from}-${to}`, fields: [],
    tables: [{
      columns: ['المندوب', 'الهدف ($)', 'صافي المبيعات ($)', 'الإنجاز %', 'الربح الإجمالي ($)', 'الهامش %', 'العمولة ($)', 'الفواتير', 'متوسط الفاتورة ($)',
        'المرتجعات %', 'التحصيل %', 'الذمم ($)', 'الزبائن'],
      rows: ranked.map((r) => [r.fullName, num(r.targetUsd), num(r.netSalesUsd), pct(r.achievementPct), num(r.grossProfitUsd), pct(r.grossMarginPct), num(r.commissionUsd),
        num(r.invoiceCount), num(r.averageInvoiceUsd), pct(r.returnRatePct), pct(r.collectionRatePct), num(r.outstandingUsd), num(r.customerCount)]),
      totals: ['الفريق', num(team.target), num(team.net), pct(teamAchievement), num(team.profit), '', num(team.commission), num(sum((r) => r.invoiceCount)), '', '', '',
        num(team.outstanding), num(sum((r) => r.customerCount))],
      numericColumns: [1, 2, 4, 6, 7, 8, 11, 12],
    }],
  });

  return (
    <Box>
      <PageHeader title="مؤشرات أداء المندوبين" subtitle="الهدف والإنجاز والربح الإجمالي والعمولة والتحصيل لكل مندوب — العمولة من الربح الإجمالي، والهدف بصافي المبيعات" />
      <Stack direction="row" gap={1.5} alignItems="center" flexWrap="wrap" sx={{ mb: 2 }}>
        <TextField size="small" type="date" label="من" value={from} onChange={(e) => setFrom(e.target.value)} InputLabelProps={{ shrink: true }} />
        <TextField size="small" type="date" label="إلى" value={to} onChange={(e) => setTo(e.target.value)} InputLabelProps={{ shrink: true }} />
        <Box sx={{ mr: 'auto' }} />
        <ExportMenu build={buildExport} disabled={reps.length === 0} />
      </Stack>
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '240px 1fr' }, gap: 2, mb: 2 }}>
        <Paper variant="outlined" sx={{ p: 2, borderRadius: 3 }}>
          <Typography fontWeight={700} sx={{ mb: 1 }}>إنجاز الفريق</Typography>
          <TargetGauge achieved={team.net} target={team.target} achievementPct={teamAchievement} size={180} />
        </Paper>
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(190px, 1fr))', gap: 2 }}>
          <KpiTile title="صافي المبيعات" value={<Money usd={team.net} variant="h5" fontWeight={800} />} hint={`من هدف ${num(team.target)} $`} />
          <KpiTile title="الربح الإجمالي" value={<Money usd={team.profit} variant="h5" fontWeight={800} />} tone="success"
            hint={team.net > 0 ? `هامش تقريبي ${Math.round((team.profit / team.net) * 1000) / 10}% من الصافي` : undefined} />
          <KpiTile title="العمولات" value={<Money usd={team.commission} variant="h5" fontWeight={800} />} hint={`${reps.length} مندوب نشط`} />
          <KpiTile title="المحصّل" value={<Money usd={team.collected} variant="h5" fontWeight={800} />} tone="success"
            hint={team.net > 0 ? `${Math.round((team.collected / team.net) * 1000) / 10}% من الصافي` : undefined} />
          <KpiTile title="ذمم الزبائن" value={<Money usd={team.outstanding} variant="h5" fontWeight={800} />} tone="warning" />
        </Box>
      </Box>

      {reps.length > 0 ? (
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1fr 1fr' }, gap: 2, mb: 2 }}>
          <Paper variant="outlined" sx={{ p: 2, borderRadius: 3 }}>
            <Typography fontWeight={700} sx={{ mb: 1 }}>نسبة الإنجاز من الهدف (%)</Typography>
            <BarChart
              height={Math.max(180, ranked.length * 42)}
              layout="horizontal"
              yAxis={[{ scaleType: 'band', data: ranked.map((r) => r.fullName) }]}
              xAxis={[{ min: 0 }]}
              series={[{ data: ranked.map((r) => r.achievementPct ?? 0), label: 'الإنجاز %', valueFormatter: (v) => `${v ?? 0}%` }]}
              margin={{ top: 30, bottom: 30, left: 120, right: 20 }}
            />
          </Paper>
          <Paper variant="outlined" sx={{ p: 2, borderRadius: 3 }}>
            <Typography fontWeight={700} sx={{ mb: 1 }}>صافي المبيعات والربح الإجمالي والعمولة ($)</Typography>
            <BarChart
              height={Math.max(180, ranked.length * 42)}
              xAxis={[{ scaleType: 'band', data: ranked.map((r) => r.fullName) }]}
              series={[
                { data: ranked.map((r) => r.targetUsd), label: 'الهدف', color: '#b0bec5' },
                { data: ranked.map((r) => r.netSalesUsd), label: 'صافي المبيعات' },
                { data: ranked.map((r) => r.grossProfitUsd), label: 'الربح الإجمالي' },
                { data: ranked.map((r) => r.commissionUsd), label: 'العمولة' },
              ]}
              margin={{ top: 40, bottom: 30, left: 60, right: 10 }}
            />
          </Paper>
        </Box>
      ) : null}

      <DataTable columns={columns} rows={ranked} getKey={(r) => r.userId} loading={loading} empty="لا يوجد مندوبون نشطون" />
    </Box>
  );
}

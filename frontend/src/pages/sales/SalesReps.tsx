/**
 * Sales reps: each rep's sales, returns, gross profit, collections, receivables, commission (on gross profit) and target for a period.
 * A rep without sales_reps:read sees only their own row. The KPI comparison of all reps is on its own tab (SalesRepKpis).
 */
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Alert, Box, Button, Chip, FormControlLabel, LinearProgress, Stack, Switch, TextField, Typography } from '@mui/material';
import { salesRepsApi, SALES_REPS, type SalesRep } from '../../api/endpoints/salesReps';
import { unwrapNode } from '../../api/apiData';
import { useLoad } from '../../hooks/useLoad';
import { useCan } from '../../hooks/useCan';
import { today } from '../../lib/money';
import { num, type ExportDocument } from '../../lib/exportClient';
import PageHeader from '../../components/ui/PageHeader';
import DataTable, { type Column } from '../../components/ui/DataTable';
import KpiTile from '../../components/ui/KpiTile';
import ExportMenu from '../../components/ui/ExportMenu';
import SalesRepDialog from '../../features/salesReps/SalesRepDialog';
import Money from '../../components/ui/Money';

const monthStart = (): string => `${today().slice(0, 8)}01`;

/** Progress towards the period target (the server's achievement %); blank when the rep has no target. */
export function TargetBar({ rep }: { rep: Pick<SalesRep, 'achievementPct' | 'targetUsd'> }): JSX.Element {
  if (rep.achievementPct === null) return <Typography variant="caption" color="text.secondary">—</Typography>;
  const pct = rep.achievementPct;
  const target = rep.targetUsd;
  return (
    <Box sx={{ minWidth: 110 }}>
      <LinearProgress variant="determinate" value={Math.min(Math.max(pct, 0), 100)} color={pct >= 100 ? 'success' : pct >= 60 ? 'primary' : 'warning'} sx={{ height: 6, borderRadius: 3 }} />
      <Typography variant="caption" color="text.secondary" component="div">{pct}% من <Money usd={target} inline variant="caption" /></Typography>
    </Box>
  );
}

export default function SalesReps(): JSX.Element {
  const navigate = useNavigate();
  const canManage = useCan(SALES_REPS.manage);
  const [from, setFrom] = useState(monthStart());
  const [to, setTo] = useState(today());
  const [inactive, setInactive] = useState(false);
  const [editing, setEditing] = useState<SalesRep | null | undefined>(undefined);

  const { data, loading, error, reload } = useLoad(
    async () => unwrapNode<SalesRep[]>((await salesRepsApi.list({ from, to, includeInactive: inactive })).data) ?? [],
    [from, to, inactive], 'تعذر تحميل المندوبين');
  const reps = data ?? [];
  const sum = (f: (r: SalesRep) => number): number => reps.reduce((a, r) => a + f(r), 0);

  const columns: Column<SalesRep>[] = [
    {
      header: 'المندوب',
      render: (r) => (
        <Box>
          <Typography fontWeight={700}>{r.fullName}</Typography>
          <Typography variant="caption" color="text.secondary">{r.userName}{r.phone ? ` · ${r.phone}` : ''}</Typography>
          {!r.isActive ? <Chip size="small" label="موقوف" sx={{ ms: 1 }} /> : null}
        </Box>
      ),
    },
    { header: 'الزبائن', render: (r) => r.customerCount, numeric: true },
    { header: 'الفواتير', render: (r) => r.invoiceCount, numeric: true },
    { header: 'المبيعات', render: (r) => <Money usd={r.salesUsd} />, numeric: true, nowrap: true },
    { header: 'المرتجعات', render: (r) => (r.returnsUsd ? <Money usd={r.returnsUsd} /> : '—'), numeric: true, nowrap: true },
    { header: 'الصافي', render: (r) => <Money usd={r.netSalesUsd} fontWeight={700} />, numeric: true, nowrap: true },
    {
      header: 'الربح الإجمالي', numeric: true, nowrap: true,
      render: (r) => <><Money usd={r.grossProfitUsd} /> {r.grossMarginPct !== null ? <Typography component="span" variant="caption" color="text.secondary">({r.grossMarginPct}%)</Typography> : null}</>,
    },
    { header: 'المحصّل', render: (r) => <Money usd={r.collectedUsd} />, numeric: true, nowrap: true },
    { header: 'ذمم الزبائن', render: (r) => <Money usd={r.outstandingUsd} />, numeric: true, nowrap: true },
    { header: 'العمولة', render: (r) => <><Money usd={r.commissionUsd} /> <Typography component="span" variant="caption" color="text.secondary">({r.commissionPct}%)</Typography></>, numeric: true, nowrap: true },
    { header: 'الهدف', render: (r) => <TargetBar rep={r} /> },
    {
      header: ' ',
      render: (r) => (
        <Stack direction="row" spacing={0.5}>
          <Button size="small" onClick={() => navigate(`/sales-reps/${r.userId}?from=${from}&to=${to}`)}>التفاصيل</Button>
          {canManage ? <Button size="small" onClick={() => setEditing(r)}>تعديل</Button> : null}
        </Stack>
      ),
      nowrap: true,
    },
  ];

  const buildExport = async (): Promise<ExportDocument> => ({
    title: 'أداء مندوبي المبيعات', subtitle: `${from} — ${to}`, fileName: `sales-reps-${from}-${to}`, fields: [],
    tables: [{
      columns: ['المندوب', 'الزبائن', 'الفواتير', 'المبيعات ($)', 'المرتجعات ($)', 'الصافي ($)', 'الربح الإجمالي ($)', 'المحصّل ($)', 'الذمم ($)', 'العمولة %', 'العمولة ($)', 'الهدف ($)'],
      rows: reps.map((r) => [r.fullName, num(r.customerCount), num(r.invoiceCount), num(r.salesUsd), num(r.returnsUsd), num(r.netSalesUsd), num(r.grossProfitUsd),
        num(r.collectedUsd), num(r.outstandingUsd), num(r.commissionPct), num(r.commissionUsd), num(r.targetUsd)]),
      totals: ['الإجمالي', '', num(sum((r) => r.invoiceCount)), num(sum((r) => r.salesUsd)), num(sum((r) => r.returnsUsd)), num(sum((r) => r.netSalesUsd)),
        num(sum((r) => r.grossProfitUsd)), num(sum((r) => r.collectedUsd)), num(sum((r) => r.outstandingUsd)), '', num(sum((r) => r.commissionUsd)), num(sum((r) => r.targetUsd))],
      numericColumns: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11],
    }],
  });

  return (
    <Box>
      <PageHeader
        title="مندوبو المبيعات"
        subtitle="المبيعات والربح الإجمالي والتحصيل والعمولة لكل مندوب — العمولة نسبة من الربح الإجمالي لفواتيره المرحّلة"
        actions={canManage ? <Button variant="contained" onClick={() => setEditing(null)}>＋ إضافة مندوب</Button> : undefined}
      />
      <Stack direction="row" gap={1.5} alignItems="center" flexWrap="wrap" sx={{ mb: 2 }}>
        <TextField size="small" type="date" label="من" value={from} onChange={(e) => setFrom(e.target.value)} InputLabelProps={{ shrink: true }} />
        <TextField size="small" type="date" label="إلى" value={to} onChange={(e) => setTo(e.target.value)} InputLabelProps={{ shrink: true }} />
        <FormControlLabel control={<Switch checked={inactive} onChange={(e) => setInactive(e.target.checked)} />} label="إظهار الموقوفين" />
        <Box sx={{ mr: 'auto' }} />
        <ExportMenu build={buildExport} disabled={reps.length === 0} />
      </Stack>
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 2, mb: 2 }}>
        <KpiTile title="صافي المبيعات" value={<Money usd={sum((r) => r.netSalesUsd)} variant="h5" fontWeight={800} />} />
        <KpiTile title="المحصّل في الفترة" value={<Money usd={sum((r) => r.collectedUsd)} variant="h5" fontWeight={800} />} tone="success" />
        <KpiTile title="ذمم زبائن المندوبين" value={<Money usd={sum((r) => r.outstandingUsd)} variant="h5" fontWeight={800} />} tone="warning" hint="المستحق اليوم على فواتيرهم" />
        <KpiTile title="الربح الإجمالي" value={<Money usd={sum((r) => r.grossProfitUsd)} variant="h5" fontWeight={800} />} tone="success" hint="بعد الخصومات وتكلفة البضاعة" />
        <KpiTile title="العمولات المستحقة" value={<Money usd={sum((r) => r.commissionUsd)} variant="h5" fontWeight={800} />} hint={`${reps.length} مندوب — من الربح الإجمالي`} />
      </Box>
      <DataTable columns={columns} rows={reps} getKey={(r) => r.userId} loading={loading} empty="لا يوجد مندوبون — أضف مستخدماً كمندوب" />
      <SalesRepDialog open={editing !== undefined} rep={editing ?? null} onClose={() => setEditing(undefined)} onSaved={reload} />
    </Box>
  );
}

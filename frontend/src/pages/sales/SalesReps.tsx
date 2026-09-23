/** Sales reps: each rep's sales, returns, collections, receivables, commission and target for a period. A rep without sales_reps:read sees only their own row. */
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Alert, Box, Button, Chip, FormControlLabel, LinearProgress, Stack, Switch, TextField, Typography } from '@mui/material';
import { salesRepsApi, SALES_REPS, type SalesRep } from '../../api/endpoints/salesReps';
import { unwrapNode } from '../../api/apiData';
import { useLoad } from '../../hooks/useLoad';
import { useCan } from '../../hooks/useCan';
import { money, today } from '../../lib/money';
import { num, type ExportDocument } from '../../lib/exportClient';
import PageHeader from '../../components/ui/PageHeader';
import DataTable, { type Column } from '../../components/ui/DataTable';
import KpiTile from '../../components/ui/KpiTile';
import ExportMenu from '../../components/ui/ExportMenu';
import SalesRepDialog from '../../features/salesReps/SalesRepDialog';

const monthStart = (): string => `${today().slice(0, 8)}01`;

/** Progress towards the period target; blank when the rep has no target. */
export function TargetBar({ value, target }: { value: number; target: number }): JSX.Element {
  if (target <= 0) return <Typography variant="caption" color="text.secondary">—</Typography>;
  const pct = Math.round((value / target) * 100);
  return (
    <Box sx={{ minWidth: 110 }}>
      <LinearProgress variant="determinate" value={Math.min(Math.max(pct, 0), 100)} color={pct >= 100 ? 'success' : pct >= 60 ? 'primary' : 'warning'} sx={{ height: 6, borderRadius: 3 }} />
      <Typography variant="caption" color="text.secondary">{pct}% من {money(target)}</Typography>
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
    { header: 'المبيعات ($)', render: (r) => money(r.salesUsd), numeric: true, nowrap: true },
    { header: 'المرتجعات ($)', render: (r) => (r.returnsUsd ? money(r.returnsUsd) : '—'), numeric: true, nowrap: true },
    { header: 'الصافي ($)', render: (r) => <b>{money(r.netSalesUsd)}</b>, numeric: true, nowrap: true },
    { header: 'المحصّل ($)', render: (r) => money(r.collectedUsd), numeric: true, nowrap: true },
    { header: 'ذمم الزبائن ($)', render: (r) => money(r.outstandingUsd), numeric: true, nowrap: true },
    { header: 'العمولة', render: (r) => <span>{money(r.commissionUsd)} <Typography component="span" variant="caption" color="text.secondary">({r.commissionPct}%)</Typography></span>, numeric: true, nowrap: true },
    { header: 'الهدف', render: (r) => <TargetBar value={r.netSalesUsd} target={r.targetUsd} /> },
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
      columns: ['المندوب', 'الزبائن', 'الفواتير', 'المبيعات ($)', 'المرتجعات ($)', 'الصافي ($)', 'المحصّل ($)', 'الذمم ($)', 'العمولة %', 'العمولة ($)', 'الهدف ($)'],
      rows: reps.map((r) => [r.fullName, num(r.customerCount), num(r.invoiceCount), num(r.salesUsd), num(r.returnsUsd), num(r.netSalesUsd), num(r.collectedUsd),
        num(r.outstandingUsd), num(r.commissionPct), num(r.commissionUsd), num(r.targetUsd)]),
      totals: ['الإجمالي', '', num(sum((r) => r.invoiceCount)), num(sum((r) => r.salesUsd)), num(sum((r) => r.returnsUsd)), num(sum((r) => r.netSalesUsd)),
        num(sum((r) => r.collectedUsd)), num(sum((r) => r.outstandingUsd)), '', num(sum((r) => r.commissionUsd)), num(sum((r) => r.targetUsd))],
      numericColumns: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
    }],
  });

  return (
    <Box>
      <PageHeader
        title="مندوبو المبيعات"
        subtitle="المبيعات والتحصيل والعمولة لكل مندوب — الفواتير المرحّلة فقط، بالدولار"
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
        <KpiTile title="صافي المبيعات" value={`$${money(sum((r) => r.netSalesUsd))}`} hint={`مرتجعات $${money(sum((r) => r.returnsUsd))}`} />
        <KpiTile title="المحصّل في الفترة" value={`$${money(sum((r) => r.collectedUsd))}`} tone="success" />
        <KpiTile title="ذمم زبائن المندوبين" value={`$${money(sum((r) => r.outstandingUsd))}`} tone="warning" hint="المستحق اليوم على فواتيرهم" />
        <KpiTile title="العمولات المستحقة" value={`$${money(sum((r) => r.commissionUsd))}`} hint={`${reps.length} مندوب`} />
      </Box>
      <DataTable columns={columns} rows={reps} getKey={(r) => r.userId} loading={loading} empty="لا يوجد مندوبون — أضف مستخدماً كمندوب" />
      <SalesRepDialog open={editing !== undefined} rep={editing ?? null} onClose={() => setEditing(undefined)} onSaved={reload} />
    </Box>
  );
}

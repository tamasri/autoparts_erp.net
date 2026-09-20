import { useState } from 'react';
import { Alert, Box, LinearProgress, Paper, Stack, TextField, Typography } from '@mui/material';
import { accountingApi, type ProfitLoss } from '../../../api/endpoints/accounting';
import { unwrapNode } from '../../../api/apiData';
import { useLoad } from '../../../hooks/useLoad';
import { money, today, yearStart } from '../../../lib/money';
import ExportMenu from '../../../components/ui/ExportMenu';
import { profitLossDocument } from '../documents';
import StatementSection from './StatementSection';

export default function ProfitLossReport(): JSX.Element {
  const [from, setFrom] = useState(yearStart());
  const [to, setTo] = useState(today());
  const { data, loading, error } = useLoad<ProfitLoss | null>(async () => unwrapNode<ProfitLoss>((await accountingApi.profitLoss(from, to)).data), [from, to], 'تعذر إعداد قائمة الأرباح والخسائر', from <= to);

  return (
    <Stack spacing={2}>
      <Stack direction="row" gap={1.5} alignItems="center" flexWrap="wrap">
        <TextField size="small" type="date" label="من" value={from} onChange={(e) => setFrom(e.target.value)} InputLabelProps={{ shrink: true }} />
        <TextField size="small" type="date" label="إلى" value={to} onChange={(e) => setTo(e.target.value)} InputLabelProps={{ shrink: true }} />
        <span style={{ marginInlineStart: 'auto' }}><ExportMenu build={async () => profitLossDocument(data!)} disabled={!data} /></span>
      </Stack>
      {loading ? <LinearProgress /> : null}
      {error ? <Alert severity="error">{error}</Alert> : null}
      {from > to ? <Alert severity="warning">تاريخ النهاية قبل البداية.</Alert> : null}
      {data ? (
        <>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 3, alignItems: 'start' }}>
            <StatementSection title="الإيرادات" lines={data.income} total={data.totalIncome} totalLabel="إجمالي الإيرادات" />
            <StatementSection title="المصروفات" lines={data.expenses} total={data.totalExpenses} totalLabel="إجمالي المصروفات" />
          </Box>
          <Paper variant="outlined" sx={{ p: 2, borderRadius: 3, textAlign: 'center', bgcolor: data.netProfit >= 0 ? 'success.light' : 'error.light' }}>
            <Typography variant="body2">{data.netProfit >= 0 ? 'صافي الربح' : 'صافي الخسارة'}</Typography>
            <Typography variant="h4" fontWeight={800} sx={{ direction: 'ltr' }}>{money(data.netProfit)} $</Typography>
          </Paper>
        </>
      ) : null}
    </Stack>
  );
}

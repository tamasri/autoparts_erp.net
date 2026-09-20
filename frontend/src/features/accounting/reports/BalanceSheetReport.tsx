import { useState } from 'react';
import { Alert, Box, Chip, LinearProgress, Stack, TextField } from '@mui/material';
import { accountingApi, type BalanceSheet } from '../../../api/endpoints/accounting';
import { unwrapNode } from '../../../api/apiData';
import { useLoad } from '../../../hooks/useLoad';
import { today } from '../../../lib/money';
import ExportMenu from '../../../components/ui/ExportMenu';
import { balanceSheetDocument } from '../documents';
import StatementSection from './StatementSection';

export default function BalanceSheetReport(): JSX.Element {
  const [asOf, setAsOf] = useState(today());
  const { data, loading, error } = useLoad<BalanceSheet | null>(async () => unwrapNode<BalanceSheet>((await accountingApi.balanceSheet(asOf)).data), [asOf], 'تعذر إعداد الميزانية العمومية');

  return (
    <Stack spacing={2}>
      <Stack direction="row" gap={1.5} alignItems="center" flexWrap="wrap">
        <TextField size="small" type="date" label="كما في" value={asOf} onChange={(e) => setAsOf(e.target.value)} InputLabelProps={{ shrink: true }} />
        {data ? <Chip color={data.isBalanced ? 'success' : 'error'} label={data.isBalanced ? 'الأصول = الالتزامات + حقوق الملكية' : 'الميزانية غير متوازنة'} /> : null}
        <span style={{ marginInlineStart: 'auto' }}><ExportMenu build={async () => balanceSheetDocument(data!)} disabled={!data} /></span>
      </Stack>
      {loading ? <LinearProgress /> : null}
      {error ? <Alert severity="error">{error}</Alert> : null}
      {data ? (
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 3, alignItems: 'start' }}>
          <StatementSection title="الأصول" lines={data.assets} total={data.totalAssets} totalLabel="إجمالي الأصول" />
          <Stack spacing={3}>
            <StatementSection title="الالتزامات" lines={data.liabilities} total={data.totalLiabilities} totalLabel="إجمالي الالتزامات" />
            <StatementSection title="حقوق الملكية" lines={data.equity} total={data.totalEquity} totalLabel="إجمالي حقوق الملكية" extra={{ label: 'أرباح الفترة (لم تُقفل بعد)', amount: data.netProfit }} />
          </Stack>
        </Box>
      ) : null}
    </Stack>
  );
}

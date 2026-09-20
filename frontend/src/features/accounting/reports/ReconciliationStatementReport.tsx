/** The classic reconciliation statement of one account: balance per the books, less/plus what has not cleared, equals the balance per the statement. */
import { useState } from 'react';
import { Alert, Chip, LinearProgress, Paper, Stack, Table, TableBody, TableCell, TableContainer, TableRow, TextField, Typography } from '@mui/material';
import { accountingApi, type ReconcileCandidate, type ReconciliationStatement } from '../../../api/endpoints/accounting';
import { unwrapNode } from '../../../api/apiData';
import { useChartAccounts } from '../../../hooks/useChartAccounts';
import { useLoad } from '../../../hooks/useLoad';
import { money, moneyOrBlank, today } from '../../../lib/money';
import DataTable, { type Column } from '../../../components/ui/DataTable';
import ExportMenu from '../../../components/ui/ExportMenu';
import AccountPicker from '../AccountPicker';
import { VOUCHER_LABEL } from '../labels';
import { reconciliationStatementDocument } from '../documents';

const COLUMNS: Column<ReconcileCandidate>[] = [
  { header: 'التاريخ', nowrap: true, render: (r) => r.postingDate },
  { header: 'المستند', render: (r) => VOUCHER_LABEL[r.voucherType ?? ''] ?? r.voucherType ?? '—' },
  { header: 'الرقم', render: (r) => <span style={{ fontFamily: 'monospace' }}>{r.voucherNo ?? '—'}</span> },
  { header: 'الطرف / البيان', render: (r) => r.party ?? r.remarks ?? '' },
  { header: 'مدين', numeric: true, render: (r) => moneyOrBlank(r.debit) },
  { header: 'دائن', numeric: true, render: (r) => moneyOrBlank(r.credit) },
];

export default function ReconciliationStatementReport(): JSX.Element {
  const { ledgers, loading: chartLoading } = useChartAccounts();
  const [account, setAccount] = useState<string | null>(null);
  const [asOf, setAsOf] = useState(today());
  const { data, loading, error } = useLoad<ReconciliationStatement | null>(
    async () => unwrapNode<ReconciliationStatement>((await accountingApi.reconciliationStatement(account!, asOf)).data), [account, asOf], 'تعذر إعداد كشف التسوية', Boolean(account));

  const debitNormal = data?.debitNormal ?? true;
  return (
    <Stack spacing={2}>
      <Stack direction="row" gap={1.5} alignItems="center" flexWrap="wrap">
        <div style={{ width: 300 }}><AccountPicker accounts={ledgers} value={account} loading={chartLoading} onChange={(a) => setAccount(a?.name ?? null)} label="الحساب (مصرف / صندوق)" /></div>
        <TextField size="small" type="date" label="كما في" value={asOf} onChange={(e) => setAsOf(e.target.value)} InputLabelProps={{ shrink: true }} />
        <span style={{ marginInlineStart: 'auto' }}><ExportMenu build={async () => reconciliationStatementDocument(data!)} disabled={!data} /></span>
      </Stack>
      {loading ? <LinearProgress /> : null}
      {error ? <Alert severity="error">{error}</Alert> : null}
      {!account ? <Alert severity="info">اختر حساباً لعرض كشف تسويته.</Alert> : null}
      {data ? (
        <>
          <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 3, maxWidth: 640 }}>
            <Table size="small">
              <TableBody>
                <TableRow><TableCell>الرصيد حسب الدفاتر</TableCell><TableCell align="left" sx={{ fontWeight: 700 }}>{money(data.bookBalance)}</TableCell></TableRow>
                <TableRow><TableCell>{debitNormal ? 'ناقص: مدين لم يُسوَّ (إيداعات بالطريق)' : 'ناقص: مدين لم يُسوَّ'}</TableCell><TableCell align="left">{money(data.unclearedDebits)}</TableCell></TableRow>
                <TableRow><TableCell>{debitNormal ? 'زائد: دائن لم يُسوَّ (شيكات لم تُصرف)' : 'زائد: دائن لم يُسوَّ'}</TableCell><TableCell align="left">{money(data.unclearedCredits)}</TableCell></TableRow>
                <TableRow sx={{ '& td': { fontWeight: 800, bgcolor: 'action.selected' } }}><TableCell>الرصيد حسب الكشف</TableCell><TableCell align="left">{money(data.statementBalance)}</TableCell></TableRow>
              </TableBody>
            </Table>
          </TableContainer>
          <Stack direction="row" gap={1} alignItems="center">
            <Chip size="small" variant="outlined" label={data.lastReconciledOn ? `آخر تسوية: ${data.lastReconciledOn}` : 'لم تُجرَ تسوية لهذا الحساب بعد'} />
            <Typography variant="caption" color="text.secondary">الأرقام بالاتجاه الطبيعي للحساب.</Typography>
          </Stack>
          <Typography variant="subtitle2" fontWeight={700}>حركات لم تُسوَّ ({data.uncleared.length})</Typography>
          <DataTable columns={COLUMNS} rows={data.uncleared} getKey={(r) => r.glName} empty="كل الحركات مُسوّاة" />
        </>
      ) : null}
    </Stack>
  );
}

import { useState } from 'react';
import { Alert, Chip, LinearProgress, Paper, Stack, Switch, FormControlLabel, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField } from '@mui/material';
import { accountingApi, type TrialBalance } from '../../../api/endpoints/accounting';
import { unwrapNode } from '../../../api/apiData';
import { useLoad } from '../../../hooks/useLoad';
import { money, moneyOrBlank, today, yearStart } from '../../../lib/money';
import ExportMenu from '../../../components/ui/ExportMenu';
import { trialBalanceDocument } from '../documents';

const debit = (v: number): number => (v > 0 ? v : 0);
const credit = (v: number): number => (v < 0 ? -v : 0);

export default function TrialBalanceReport(): JSX.Element {
  const [from, setFrom] = useState(yearStart());
  const [to, setTo] = useState(today());
  const [includeZero, setIncludeZero] = useState(false);
  const { data, loading, error } = useLoad<TrialBalance | null>(
    async () => unwrapNode<TrialBalance>((await accountingApi.trialBalance(from, to, includeZero)).data), [from, to, includeZero], 'تعذر إعداد ميزان المراجعة', from <= to);

  return (
    <Stack spacing={2}>
      <Stack direction="row" gap={1.5} alignItems="center" flexWrap="wrap">
        <TextField size="small" type="date" label="من" value={from} onChange={(e) => setFrom(e.target.value)} InputLabelProps={{ shrink: true }} />
        <TextField size="small" type="date" label="إلى" value={to} onChange={(e) => setTo(e.target.value)} InputLabelProps={{ shrink: true }} />
        <FormControlLabel label="إظهار الحسابات الصفرية" control={<Switch size="small" checked={includeZero} onChange={(e) => setIncludeZero(e.target.checked)} />} />
        {data ? <Chip color={data.isBalanced ? 'success' : 'error'} label={data.isBalanced ? 'متوازن' : 'غير متوازن'} /> : null}
        <span style={{ marginInlineStart: 'auto' }}><ExportMenu build={async () => trialBalanceDocument(data!)} disabled={!data} /></span>
      </Stack>
      {loading ? <LinearProgress /> : null}
      {error ? <Alert severity="error">{error}</Alert> : null}
      {from > to ? <Alert severity="warning">تاريخ النهاية قبل البداية.</Alert> : null}
      {data ? (
        <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 3 }}>
          <Table size="small">
            <TableHead>
              <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover', textAlign: 'center' } }}>
                <TableCell rowSpan={2} sx={{ textAlign: 'right !important' }}>الحساب</TableCell>
                <TableCell colSpan={2}>الرصيد الافتتاحي</TableCell><TableCell colSpan={2}>حركة الفترة</TableCell><TableCell colSpan={2}>الرصيد الختامي</TableCell>
              </TableRow>
              <TableRow sx={{ '& th': { fontWeight: 600, bgcolor: 'action.hover' } }}>
                {['مدين', 'دائن', 'مدين', 'دائن', 'مدين', 'دائن'].map((h, i) => <TableCell key={i} align="left">{h}</TableCell>)}
              </TableRow>
            </TableHead>
            <TableBody>
              {data.lines.length === 0 ? <TableRow><TableCell colSpan={7} align="center" sx={{ py: 5, color: 'text.secondary' }}>لا توجد حركات في هذه الفترة</TableCell></TableRow> : null}
              {data.lines.map((l) => (
                <TableRow key={l.account} hover sx={l.isGroup ? { bgcolor: 'action.hover' } : undefined}>
                  <TableCell sx={{ paddingInlineStart: `${16 + l.depth * 20}px`, fontWeight: l.isGroup ? 700 : 400 }}>{l.accountName}</TableCell>
                  <TableCell align="left">{moneyOrBlank(debit(l.opening))}</TableCell><TableCell align="left">{moneyOrBlank(credit(l.opening))}</TableCell>
                  <TableCell align="left">{moneyOrBlank(l.debit)}</TableCell><TableCell align="left">{moneyOrBlank(l.credit)}</TableCell>
                  <TableCell align="left" sx={{ fontWeight: 700 }}>{moneyOrBlank(debit(l.closing))}</TableCell><TableCell align="left" sx={{ fontWeight: 700 }}>{moneyOrBlank(credit(l.closing))}</TableCell>
                </TableRow>
              ))}
              <TableRow sx={{ '& td': { fontWeight: 800, bgcolor: 'action.selected' } }}>
                <TableCell colSpan={5}>الإجمالي (الأرصدة الختامية)</TableCell><TableCell align="left">{money(data.totalDebit)}</TableCell><TableCell align="left">{money(data.totalCredit)}</TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </TableContainer>
      ) : null}
    </Stack>
  );
}

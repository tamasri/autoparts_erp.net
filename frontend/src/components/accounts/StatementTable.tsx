/** Account statement table shared by the customer page and the account (party) statement page: SYP + USD with running balance. */
import { Box, Chip, Paper, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import type { ExportDocument } from '../../lib/exportClient';
import { num, ymd } from '../../lib/exportClient';

export type StatementLine = {
  id?: string;
  date: string;
  type: string;
  reference?: string;
  description?: string;
  debitSyp: number;
  creditSyp: number;
  debitUsd: number;
  creditUsd: number;
  balanceSyp?: number;
  balanceUsd?: number;
};

export const TYPE_LABEL: Record<string, string> = {
  INVOICE: 'فاتورة', VOIDED: 'فاتورة (ملغاة)', CREDIT_NOTE: 'إشعار دائن', RETURN: 'مرتجع', PAYMENT: 'سند قبض', REFUND: 'ردّ مبلغ', BILL: 'فاتورة شراء', SUPPLIER_PAYMENT: 'دفعة لمورّد',
};

const money = (v: number | undefined): string => (v === undefined || v === 0 ? '—' : v.toLocaleString('en-US', { maximumFractionDigits: 2 }));

/** Adds a running balance when the source does not provide one. */
export function withRunning(lines: StatementLine[]): StatementLine[] {
  let s = 0; let u = 0;
  return lines.map((l) => {
    s += l.debitSyp - l.creditSyp; u += l.debitUsd - l.creditUsd;
    return { ...l, balanceSyp: l.balanceSyp ?? s, balanceUsd: l.balanceUsd ?? u };
  });
}

export function statementDocument(title: string, subtitle: string, fields: Array<{ label: string; value: string }>, lines: StatementLine[], sectionTitle = 'كشف الحساب'): ExportDocument {
  const totals = lines.reduce((a, l) => ({ ds: a.ds + l.debitSyp, cs: a.cs + l.creditSyp, du: a.du + l.debitUsd, cu: a.cu + l.creditUsd }), { ds: 0, cs: 0, du: 0, cu: 0 });
  const last = lines[lines.length - 1];
  return {
    title, subtitle, fileName: title.replace(/\s+/g, '-'),
    fields,
    tables: [{
      title: sectionTitle,
      columns: ['التاريخ', 'النوع', 'المرجع', 'مدين (ل.س)', 'دائن (ل.س)', 'الرصيد (ل.س)', 'مدين ($)', 'دائن ($)', 'الرصيد ($)'],
      rows: lines.map((l) => [ymd(l.date), TYPE_LABEL[l.type] ?? l.type, l.reference ?? l.description ?? '', num(l.debitSyp), num(l.creditSyp), num(l.balanceSyp), num(l.debitUsd), num(l.creditUsd), num(l.balanceUsd)]),
      totals: ['', '', 'الإجمالي', num(totals.ds), num(totals.cs), num(last?.balanceSyp ?? 0), num(totals.du), num(totals.cu), num(last?.balanceUsd ?? 0)],
      numericColumns: [3, 4, 5, 6, 7, 8],
    }],
  };
}

export default function StatementTable({ lines, linkFor }: { lines: StatementLine[]; linkFor?: (l: StatementLine) => string | null }): JSX.Element {
  return (
    <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 3 }}>
      <Table size="small">
        <TableHead>
          <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
            <TableCell>التاريخ</TableCell><TableCell>النوع</TableCell><TableCell>المرجع</TableCell>
            <TableCell align="left">مدين (ل.س)</TableCell><TableCell align="left">دائن (ل.س)</TableCell><TableCell align="left">الرصيد (ل.س)</TableCell>
            <TableCell align="left">مدين ($)</TableCell><TableCell align="left">دائن ($)</TableCell><TableCell align="left">الرصيد ($)</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {lines.length === 0 ? (
            <TableRow><TableCell colSpan={9} align="center" sx={{ py: 5, color: 'text.secondary' }}>لا توجد حركات مالية</TableCell></TableRow>
          ) : lines.map((l, i) => {
            const to = linkFor?.(l) ?? null;
            return (
              <TableRow key={`${l.id ?? l.reference}-${i}`} hover>
                <TableCell>{ymd(l.date)}</TableCell>
                <TableCell><Chip size="small" label={TYPE_LABEL[l.type] ?? l.type} color={l.type === 'PAYMENT' || l.type === 'RETURN' || l.type === 'CREDIT_NOTE' ? 'success' : 'default'} variant="outlined" /></TableCell>
                <TableCell sx={{ fontWeight: 600 }}>
                  {to ? <Box component={RouterLink} to={to} sx={{ color: 'primary.main', textDecoration: 'none' }}>{l.reference || l.description || '—'}</Box> : (l.reference || l.description || '—')}
                </TableCell>
                <TableCell align="left" sx={{ color: l.debitSyp ? 'error.main' : 'text.disabled' }}>{money(l.debitSyp)}</TableCell>
                <TableCell align="left" sx={{ color: l.creditSyp ? 'success.main' : 'text.disabled' }}>{money(l.creditSyp)}</TableCell>
                <TableCell align="left" sx={{ fontWeight: 700 }}>{(l.balanceSyp ?? 0).toLocaleString('en-US', { maximumFractionDigits: 2 })}</TableCell>
                <TableCell align="left" sx={{ color: l.debitUsd ? 'error.main' : 'text.disabled' }}>{money(l.debitUsd)}</TableCell>
                <TableCell align="left" sx={{ color: l.creditUsd ? 'success.main' : 'text.disabled' }}>{money(l.creditUsd)}</TableCell>
                <TableCell align="left" sx={{ fontWeight: 700 }}>{(l.balanceUsd ?? 0).toLocaleString('en-US', { maximumFractionDigits: 2 })}</TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', p: 1.5 }}>
        الرصيد الموجب = مستحق على الحساب لصالحنا، والسالب = رصيد دائن للحساب.
      </Typography>
    </TableContainer>
  );
}

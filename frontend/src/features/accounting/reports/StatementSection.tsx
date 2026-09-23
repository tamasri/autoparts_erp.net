/** One block of a financial statement (assets, expenses, ...): indented accounts and a total. */
import { Paper, Table, TableBody, TableCell, TableContainer, TableHead, TableRow } from '@mui/material';
import type { StatementLine } from '../../../api/endpoints/accounting';
import Money from '../../../components/ui/Money';

type Props = { title: string; lines: StatementLine[]; total: number; totalLabel: string; extra?: { label: string; amount: number } };

export default function StatementSection({ title, lines, total, totalLabel, extra }: Props): JSX.Element {
  return (
    <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 3 }}>
      <Table size="small">
        <TableHead><TableRow sx={{ '& th': { fontWeight: 800, bgcolor: 'action.hover' } }}><TableCell>{title}</TableCell><TableCell align="left">$</TableCell></TableRow></TableHead>
        <TableBody>
          {lines.length === 0 && !extra ? <TableRow><TableCell colSpan={2} align="center" sx={{ color: 'text.secondary', py: 2 }}>—</TableCell></TableRow> : null}
          {lines.map((l) => (
            <TableRow key={l.account} hover>
              <TableCell sx={{ paddingInlineStart: `${16 + l.depth * 20}px`, fontWeight: l.isGroup ? 700 : 400 }}>{l.accountName}</TableCell>
              <TableCell align="left"><Money usd={l.amount} fontWeight={l.isGroup ? 700 : 400} /></TableCell>
            </TableRow>
          ))}
          {extra ? <TableRow hover><TableCell sx={{ fontStyle: 'italic' }}>{extra.label}</TableCell><TableCell align="left"><Money usd={extra.amount} /></TableCell></TableRow> : null}
          <TableRow sx={{ '& td': { fontWeight: 800, bgcolor: 'action.selected' } }}><TableCell>{totalLabel}</TableCell><TableCell align="left"><Money usd={total} fontWeight={800} /></TableCell></TableRow>
        </TableBody>
      </Table>
    </TableContainer>
  );
}

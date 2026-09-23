/** Read-only view of an entry (draft, posted or voided) with its lines, ledger status, and print / export. */
import { useEffect, useState } from 'react';
import { Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import { accountingApi, type JournalEntryDetail } from '../../api/endpoints/accounting';
import { unwrapNode } from '../../api/apiData';
import { extractApiError } from '../../lib/toast';
import ExportMenu from '../../components/ui/ExportMenu';
import StatusChip from '../../components/ui/StatusChip';
import { SYNC_LABEL } from './labels';
import { entryDocument } from './documents';
import Money from '../../components/ui/Money';

export default function EntryViewDialog({ entryId, onClose }: { entryId: string | null; onClose: () => void }): JSX.Element {
  const [detail, setDetail] = useState<JournalEntryDetail | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    setDetail(null); setError('');
    if (!entryId) return;
    accountingApi.entry(entryId).then((r) => setDetail(unwrapNode<JournalEntryDetail>(r.data))).catch((e: unknown) => setError(extractApiError(e, 'تعذر تحميل القيد')));
  }, [entryId]);

  const e = detail?.entry;
  const sync = e ? SYNC_LABEL[e.syncStatus] : null;

  return (
    <Dialog open={Boolean(entryId)} onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>{e ? `${e.typeNameAr} ${e.entryNumber}` : 'قيد'}</DialogTitle>
      <DialogContent dividers>
        {error ? <Alert severity="error">{error}</Alert> : null}
        {e && detail ? (
          <Stack spacing={2}>
            <Stack direction="row" gap={1} flexWrap="wrap" alignItems="center">
              <StatusChip status={e.status} />
              {sync && e.status !== 'DRAFT' ? <Chip size="small" color={sync.color} label={sync.label} /> : null}
              <Typography variant="body2" color="text.secondary">{e.entryDate}</Typography>
              {e.erpNextName ? <Typography variant="body2" sx={{ fontFamily: 'monospace' }}>{e.erpNextName}</Typography> : null}
            </Stack>
            {e.syncError ? <Alert severity="error">{e.syncError}</Alert> : null}
            {e.narration ? <Typography>{e.narration}</Typography> : null}
            {detail.voidReason ? <Alert severity="warning">سبب الإلغاء: {detail.voidReason}</Alert> : null}
            <Box sx={{ overflowX: 'auto' }}>
              <Table size="small">
                <TableHead><TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
                  <TableCell>#</TableCell><TableCell>الحساب</TableCell><TableCell>الزبون / المورّد</TableCell><TableCell align="left">مدين</TableCell><TableCell align="left">دائن</TableCell><TableCell>بيان</TableCell>
                </TableRow></TableHead>
                <TableBody>
                  {detail.lines.map((l) => (
                    <TableRow key={l.lineNumber}>
                      <TableCell>{l.lineNumber}</TableCell><TableCell>{l.account}</TableCell><TableCell>{l.partyName ?? '—'}</TableCell>
                      <TableCell align="left">{l.debit ? <Money usd={l.debit} /> : ''}</TableCell><TableCell align="left">{l.credit ? <Money usd={l.credit} /> : ''}</TableCell><TableCell>{l.narration ?? ''}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Box>
          </Stack>
        ) : null}
      </DialogContent>
      <DialogActions>
        {detail ? <ExportMenu build={async () => entryDocument(detail)} label="⬇ طباعة / تصدير" /> : null}
        <Button onClick={onClose}>إغلاق</Button>
      </DialogActions>
    </Dialog>
  );
}

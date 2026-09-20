/**
 * One viewer for every document (transfer, issue order, cycle count, adjustment, receipt, statement, movements...).
 * Shows exactly what will be printed and offers Print / PDF / Excel / CSV through the shared server engine.
 */
import { useState } from 'react';
import {
  Box, Button, ButtonGroup, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle,
  Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography,
} from '@mui/material';
import { downloadDocument, previewPdf, printDocument, type ExportDocument, type ExportFormat } from '../../lib/exportClient';

type Props = {
  open: boolean;
  onClose: () => void;
  /** null while loading */
  document: ExportDocument | null;
  loading?: boolean;
};

const fmt = (table: { numericColumns?: number[] }, col: number, v?: string | null): string => {
  if (v === null || v === undefined || v === '') return '';
  if (table.numericColumns?.includes(col)) {
    const n = Number(v);
    if (!Number.isNaN(n)) return n.toLocaleString('en-US', { maximumFractionDigits: 4 });
  }
  return String(v);
};

export default function DocumentDialog({ open, onClose, document: doc, loading }: Props): JSX.Element {
  const [busy, setBusy] = useState('');

  async function run(kind: 'print' | 'preview' | ExportFormat): Promise<void> {
    if (!doc) return;
    setBusy(kind);
    try {
      if (kind === 'print') await printDocument(doc);
      else if (kind === 'preview') await previewPdf(doc);
      else await downloadDocument(kind, doc);
    } finally {
      setBusy('');
    }
  }

  return (
    <Dialog open={open} onClose={onClose} maxWidth="lg" fullWidth scroll="paper">
      <DialogTitle sx={{ pb: 0.5 }}>
        <Typography variant="h6" fontWeight={800}>{doc?.title ?? '...'}</Typography>
        {doc?.subtitle ? <Typography variant="body2" color="text.secondary">{doc.subtitle}</Typography> : null}
      </DialogTitle>
      <DialogContent dividers>
        {loading || !doc ? (
          <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>
        ) : (
          <Stack spacing={2.5}>
            {doc.fields.length > 0 ? (
              <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 2 }}>
                {doc.fields.map((f) => (
                  <Box key={f.label}>
                    <Typography variant="caption" color="text.secondary">{f.label}</Typography>
                    <Typography variant="body2" fontWeight={700}>{f.value || '-'}</Typography>
                  </Box>
                ))}
              </Box>
            ) : null}
            {doc.tables.map((t, ti) => (
              <Box key={ti}>
                {t.title ? <Typography variant="subtitle1" fontWeight={700} sx={{ mb: 1 }}>{t.title}</Typography> : null}
                <TableContainer sx={{ border: 1, borderColor: 'divider', borderRadius: 2 }}>
                  <Table size="small">
                    <TableHead>
                      <TableRow sx={{ '& th': { bgcolor: 'primary.main', color: 'primary.contrastText', fontWeight: 700 } }}>
                        {t.columns.map((c) => <TableCell key={c}>{c}</TableCell>)}
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {t.rows.length === 0 ? (
                        <TableRow><TableCell colSpan={t.columns.length} align="center" sx={{ color: 'text.secondary', py: 3 }}>لا توجد بيانات</TableCell></TableRow>
                      ) : t.rows.map((r, ri) => (
                        <TableRow key={ri} hover>
                          {t.columns.map((_, ci) => <TableCell key={ci}>{fmt(t, ci, r[ci])}</TableCell>)}
                        </TableRow>
                      ))}
                      {t.totals ? (
                        <TableRow sx={{ '& td': { bgcolor: 'action.hover', fontWeight: 800 } }}>
                          {t.columns.map((_, ci) => <TableCell key={ci}>{fmt(t, ci, t.totals?.[ci])}</TableCell>)}
                        </TableRow>
                      ) : null}
                    </TableBody>
                  </Table>
                </TableContainer>
              </Box>
            ))}
            {doc.footer ? <Typography variant="body2" color="text.secondary">{doc.footer}</Typography> : null}
          </Stack>
        )}
      </DialogContent>
      <DialogActions sx={{ justifyContent: 'space-between', px: 3, py: 1.5 }}>
        <ButtonGroup variant="outlined" size="small" disabled={!doc || loading}>
          <Button onClick={() => void run('print')} disabled={busy !== ''}>🖨 طباعة</Button>
          <Button onClick={() => void run('preview')} disabled={busy !== ''}>👁 معاينة PDF</Button>
          <Button onClick={() => void run('pdf')} disabled={busy !== ''}>⬇ PDF</Button>
          <Button onClick={() => void run('xlsx')} disabled={busy !== ''}>⬇ Excel</Button>
          <Button onClick={() => void run('csv')} disabled={busy !== ''}>⬇ CSV</Button>
        </ButtonGroup>
        <Button onClick={onClose}>إغلاق</Button>
      </DialogActions>
    </Dialog>
  );
}

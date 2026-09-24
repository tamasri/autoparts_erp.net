/**
 * One viewer for every document (invoice, receipt, statement, transfer, issue order, count, adjustment, movements...).
 * Shows the printed PDF itself (so what you see is exactly what prints), and offers Print / open in a tab / PDF / Excel / CSV.
 * Where the browser has no PDF viewer in a page (phones), it shows the document's data as a table instead.
 * `pdf` is the document's own printed form (official documents); without it the PDF comes from the shared export engine.
 */
import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Alert, Box, Button, ButtonGroup, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle,
  Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography,
} from '@mui/material';
import {
  downloadDocument, downloadPdf, exportPdf, previewPdfFrom, printPdf, type ExportDocument, type ExportFormat, type PdfSource,
} from '../../lib/exportClient';
import { extractApiError } from '../../lib/toast';

type Props = {
  open: boolean;
  onClose: () => void;
  /** null while loading */
  document: ExportDocument | null;
  loading?: boolean;
  /** Shown beside the title, e.g. stepping to the previous / next document by number. */
  toolbar?: React.ReactNode;
  /** The document's own printed form; keep it stable (memoised) — a new function reloads the preview. */
  pdf?: PdfSource | null;
};

const fmt = (table: { numericColumns?: number[] }, col: number, v?: string | null): string => {
  if (v === null || v === undefined || v === '') return '';
  if (table.numericColumns?.includes(col)) {
    const n = Number(v);
    if (!Number.isNaN(n)) return n.toLocaleString('en-US', { maximumFractionDigits: 4 });
  }
  return String(v);
};

/** A PDF shown inside the page needs the browser's own viewer (desktop Chrome, Edge, Firefox, Safari). */
const inlineViewer = (): boolean =>
  ((navigator as Navigator & { pdfViewerEnabled?: boolean }).pdfViewerEnabled ?? false) && !/android|iphone|ipad|ipod|mobile/i.test(navigator.userAgent);

export default function DocumentDialog({ open, onClose, document: doc, loading, toolbar, pdf }: Props): JSX.Element {
  const [busy, setBusy] = useState('');
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [previewError, setPreviewError] = useState('');
  const blobRef = useRef<Blob | null>(null);
  const showPdf = useMemo(inlineViewer, []);

  const source = useMemo<PdfSource | null>(() => pdf ?? (doc ? exportPdf(doc) : null), [pdf, doc]);
  // Print and download reuse the PDF already fetched for the preview.
  const cached = useMemo<PdfSource | null>(() => (source ? () => (blobRef.current ? Promise.resolve(blobRef.current) : source()) : null), [source]);
  const fileName = doc?.fileName ?? doc?.title ?? 'document';

  useEffect(() => {
    blobRef.current = null;
    setPreviewUrl(null);
    setPreviewError('');
    if (!open || !showPdf || !source || loading) return undefined;
    let live = true;
    let url: string | null = null;
    source()
      .then((blob) => {
        if (!live) return;
        blobRef.current = blob;
        url = URL.createObjectURL(blob.type === 'application/pdf' ? blob : new Blob([blob], { type: 'application/pdf' }));
        setPreviewUrl(url);
      })
      .catch((e: unknown) => { if (live) setPreviewError(extractApiError(e, 'تعذر تجهيز المعاينة')); });
    return () => { live = false; if (url) URL.revokeObjectURL(url); };
  }, [open, showPdf, source, loading]);

  async function run(kind: 'print' | 'tab' | ExportFormat): Promise<void> {
    if (!cached) return;
    setBusy(kind);
    try {
      if (kind === 'print') await printPdf(cached);
      else if (kind === 'tab') await previewPdfFrom(cached);
      else if (kind === 'pdf') await downloadPdf(cached, fileName);
      else if (doc) await downloadDocument(kind, doc);
    } finally {
      setBusy('');
    }
  }

  const waiting = loading || (!doc && !pdf);

  return (
    <Dialog open={open} onClose={onClose} maxWidth="lg" fullWidth scroll="paper">
      <DialogTitle sx={{ pb: 0.5 }}>
        <Stack direction="row" gap={2} alignItems="center" flexWrap="wrap">
          <Box>
            <Typography variant="h6" fontWeight={800}>{doc?.title ?? '...'}</Typography>
            {doc?.subtitle ? <Typography variant="body2" color="text.secondary">{doc.subtitle}</Typography> : null}
          </Box>
          {toolbar ? <Box sx={{ mr: 'auto' }}>{toolbar}</Box> : null}
        </Stack>
      </DialogTitle>
      <DialogContent dividers sx={showPdf ? { p: 0, bgcolor: 'grey.200' } : undefined}>
        {waiting ? (
          <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>
        ) : showPdf ? (
          previewError ? <Alert severity="error" sx={{ m: 2 }}>{previewError}</Alert>
            : previewUrl ? <Box component="iframe" title={doc?.title ?? 'PDF'} src={previewUrl} sx={{ display: 'block', width: '100%', height: '75vh', border: 0 }} />
              : <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>
        ) : doc && doc.fields.length === 0 && doc.tables.length === 0 ? (
          <Alert severity="info" sx={{ m: 2 }}>المعاينة داخل الصفحة غير متاحة على هذا الجهاز — استخدم «فتح في نافذة» أو «PDF».</Alert>
        ) : doc ? (
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
                      <TableRow>{t.columns.map((c) => <TableCell key={c}>{c}</TableCell>)}</TableRow>
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
        ) : null}
      </DialogContent>
      <DialogActions sx={{ justifyContent: 'space-between', px: 3, py: 1.5 }}>
        <ButtonGroup variant="outlined" size="small" disabled={waiting || busy !== ''}>
          <Button onClick={() => void run('print')}>🖨 طباعة</Button>
          <Button onClick={() => void run('tab')}>↗ فتح في نافذة</Button>
          <Button onClick={() => void run('pdf')}>⬇ PDF</Button>
          {doc && doc.tables.length > 0 ? <Button onClick={() => void run('xlsx')}>⬇ Excel</Button> : null}
          {doc && doc.tables.length > 0 ? <Button onClick={() => void run('csv')}>⬇ CSV</Button> : null}
        </ButtonGroup>
        <Button onClick={onClose}>إغلاق</Button>
      </DialogActions>
    </Dialog>
  );
}

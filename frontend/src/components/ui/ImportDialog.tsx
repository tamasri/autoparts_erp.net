/** Import from Excel/CSV: download the template, check the file (nothing is written), then import. Shared by every bulk-load screen. */
import { useState } from 'react';
import {
  Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, LinearProgress, Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography,
} from '@mui/material';
import { extractApiError, toast } from '../../lib/toast';
import { saveBlob } from '../../lib/exportClient';

export type ImportRow = { rowNumber: number; label?: string; status: 'ok' | 'skipped' | 'error'; message?: string | null };
export type ImportSummary = { dryRun: boolean; total: number; created: number; skipped: number; failed: number; rows: ImportRow[] };

const COLOR: Record<ImportRow['status'], 'success' | 'error' | 'warning'> = { ok: 'success', error: 'error', skipped: 'warning' };
const LABEL: Record<ImportRow['status'], string> = { ok: 'سليم', error: 'خطأ', skipped: 'متجاوز' };

type Props = {
  open: boolean;
  title: string;
  /** What the user should know before uploading. */
  intro: string;
  /** Singular name of what is imported, for the success message ("صنف", "حساب"). */
  noun: string;
  /** Header of the row-label column ("الرمز", "الحساب"). */
  labelHeader: string;
  templateFileName: string;
  downloadTemplate: (format: 'xlsx' | 'csv') => Promise<Blob>;
  upload: (file: File, dryRun: boolean) => Promise<ImportSummary>;
  onClose: () => void;
  onImported: () => void;
};

export default function ImportDialog({ open, title, intro, noun, labelHeader, templateFileName, downloadTemplate, upload, onClose, onImported }: Props): JSX.Element {
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<ImportSummary | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  function reset(): void { setFile(null); setResult(null); setError(''); }

  async function template(format: 'xlsx' | 'csv'): Promise<void> {
    try { saveBlob(await downloadTemplate(format), `${templateFileName}.${format}`); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر تنزيل القالب')); }
  }

  async function run(dryRun: boolean): Promise<void> {
    if (!file) return;
    setBusy(true); setError('');
    try {
      const data = await upload(file, dryRun);
      setResult(data);
      if (!dryRun) { toast.success(`تم استيراد ${data.created} ${noun}`); onImported(); }
    } catch (e: unknown) {
      setError(extractApiError(e, 'تعذر معالجة الملف'));
    } finally {
      setBusy(false);
    }
  }

  const problems = result?.rows.filter((r) => r.status !== 'ok') ?? [];

  return (
    <Dialog open={open} onClose={() => { reset(); onClose(); }} fullWidth maxWidth="md">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2}>
          <Alert severity="info">{intro}</Alert>
          <Stack direction="row" gap={1} flexWrap="wrap" alignItems="center">
            <Button size="small" variant="outlined" onClick={() => void template('xlsx')}>⬇ قالب Excel</Button>
            <Button size="small" variant="outlined" onClick={() => void template('csv')}>⬇ قالب CSV</Button>
            <Button size="small" variant="contained" component="label">
              اختيار ملف
              <input hidden type="file" accept=".xlsx,.csv" onChange={(e) => { setFile(e.target.files?.[0] ?? null); setResult(null); setError(''); e.target.value = ''; }} />
            </Button>
            {file ? <Chip label={`${file.name} (${Math.ceil(file.size / 1024)} KB)`} onDelete={reset} /> : <Typography variant="caption" color="text.secondary">لم يُختر ملف</Typography>}
          </Stack>
          {busy ? <LinearProgress /> : null}
          {error ? <Alert severity="error">{error}</Alert> : null}

          {result ? (
            <>
              <Stack direction="row" gap={1} flexWrap="wrap">
                <Chip label={`إجمالي الصفوف: ${result.total}`} />
                <Chip color="success" label={`${result.dryRun ? 'جاهز للاستيراد' : 'تم إنشاؤه'}: ${result.created}`} />
                <Chip color="warning" label={`موجود / متجاوز: ${result.skipped}`} />
                <Chip color="error" label={`أخطاء: ${result.failed}`} />
              </Stack>
              {problems.length > 0 ? (
                <Box sx={{ maxHeight: 320, overflow: 'auto', border: 1, borderColor: 'divider', borderRadius: 2 }}>
                  <Table size="small" stickyHeader>
                    <TableHead><TableRow><TableCell>السطر</TableCell><TableCell>{labelHeader}</TableCell><TableCell>الحالة</TableCell><TableCell>الملاحظة</TableCell></TableRow></TableHead>
                    <TableBody>
                      {problems.map((r) => (
                        <TableRow key={r.rowNumber}>
                          <TableCell>{r.rowNumber}</TableCell><TableCell>{r.label ?? '—'}</TableCell>
                          <TableCell><Chip size="small" color={COLOR[r.status]} label={LABEL[r.status]} /></TableCell><TableCell>{r.message}</TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </Box>
              ) : <Alert severity="success">لا توجد ملاحظات على الصفوف.</Alert>}
            </>
          ) : null}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={() => { reset(); onClose(); }}>إغلاق</Button>
        <Button variant="outlined" disabled={!file || busy} onClick={() => void run(true)}>فحص الملف</Button>
        <Button variant="contained" disabled={!file || busy || !result || !result.dryRun || result.created === 0} onClick={() => void run(false)}>
          استيراد {result?.dryRun ? `(${result.created})` : ''}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

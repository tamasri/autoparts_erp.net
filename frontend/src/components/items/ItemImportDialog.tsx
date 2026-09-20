/** Item import from Excel/CSV: download the template, check the file (nothing is written), then import. */
import { useState } from 'react';
import {
  Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, LinearProgress, Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography,
} from '@mui/material';
import { itemsApi } from '../../api/endpoints/items';
import { unwrapNode } from '../../api/apiData';
import { extractApiError, toast } from '../../lib/toast';
import { saveBlob } from '../../lib/exportClient';

type RowResult = { rowNumber: number; code?: string; status: 'OK' | 'ERROR' | 'DUPLICATE'; message?: string };
type Result = { dryRun: boolean; total: number; created: number; duplicates: number; failed: number; rows: RowResult[] };

const COLOR: Record<RowResult['status'], 'success' | 'error' | 'warning'> = { OK: 'success', ERROR: 'error', DUPLICATE: 'warning' };
const LABEL: Record<RowResult['status'], string> = { OK: 'سليم', ERROR: 'خطأ', DUPLICATE: 'مكرر' };

export default function ItemImportDialog({ open, onClose, onImported }: { open: boolean; onClose: () => void; onImported: () => void }): JSX.Element {
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<Result | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  function reset(): void { setFile(null); setResult(null); setError(''); }

  async function template(format: 'xlsx' | 'csv'): Promise<void> {
    try {
      const res = await itemsApi.importTemplate(format);
      saveBlob(res.data as Blob, `items-template.${format}`);
    } catch (e: unknown) { toast.error(extractApiError(e, 'تعذر تنزيل القالب')); }
  }

  async function run(dryRun: boolean): Promise<void> {
    if (!file) return;
    setBusy(true); setError('');
    try {
      const res = await itemsApi.importFile(file, dryRun);
      const data = unwrapNode<Result>(res.data);
      setResult(data);
      if (!dryRun && data) {
        toast.success(`تم استيراد ${data.created} صنف`);
        onImported();
      }
    } catch (e: unknown) {
      setError(extractApiError(e, 'تعذر معالجة الملف'));
    } finally {
      setBusy(false);
    }
  }

  const problems = result?.rows.filter((r) => r.status !== 'OK') ?? [];

  return (
    <Dialog open={open} onClose={() => { reset(); onClose(); }} fullWidth maxWidth="md">
      <DialogTitle>استيراد أصناف من Excel / CSV</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2}>
          <Alert severity="info">
            حمّل القالب، عبّئ الأصناف، ثم ارفعه. يُفحص الملف أولاً دون كتابة أي شيء. الأصناف الموجودة تُتجاوز ولا تُعدَّل، وإعادة رفع نفس الملف لا تُكرّر شيئاً.
          </Alert>
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
                <Chip color="warning" label={`مكرر / متجاوز: ${result.duplicates}`} />
                <Chip color="error" label={`أخطاء: ${result.failed}`} />
              </Stack>
              {problems.length > 0 ? (
                <Box sx={{ maxHeight: 320, overflow: 'auto', border: 1, borderColor: 'divider', borderRadius: 2 }}>
                  <Table size="small" stickyHeader>
                    <TableHead><TableRow><TableCell>السطر</TableCell><TableCell>الرمز</TableCell><TableCell>الحالة</TableCell><TableCell>الملاحظة</TableCell></TableRow></TableHead>
                    <TableBody>
                      {problems.map((r) => (
                        <TableRow key={r.rowNumber}>
                          <TableCell>{r.rowNumber}</TableCell><TableCell sx={{ fontFamily: 'monospace' }}>{r.code ?? '—'}</TableCell>
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

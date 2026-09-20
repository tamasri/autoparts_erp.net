/** The entry types the business files its manual entries under. New ones can be added; built-in ones keep their kind and prefix. */
import { useState } from 'react';
import {
  Alert, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Stack, Switch, Table, TableBody, TableCell, TableHead, TableRow, TextField,
} from '@mui/material';
import { accountingApi, type EntryKind, type EntryType } from '../../api/endpoints/accounting';
import { extractApiError, toast } from '../../lib/toast';
import { KIND_LABEL } from './labels';

type Form = { id: string | null; nameAr: string; kind: EntryKind; prefix: string; description: string; isActive: boolean; isSystem: boolean };
const EMPTY: Form = { id: null, nameAr: '', kind: 'JOURNAL', prefix: '', description: '', isActive: true, isSystem: false };

export default function EntryTypesDialog({ open, types, canEdit, onClose, onChanged }: { open: boolean; types: EntryType[]; canEdit: boolean; onClose: () => void; onChanged: () => void }): JSX.Element {
  const [form, setForm] = useState<Form | null>(null);
  const [error, setError] = useState('');

  async function save(): Promise<void> {
    if (!form) return;
    if (form.nameAr.trim().length < 2) { setError('اكتب اسم النوع'); return; }
    if (!/^[A-Za-z]{1,6}$/.test(form.prefix)) { setError('البادئة من 1 إلى 6 أحرف إنجليزية'); return; }
    const body = { nameAr: form.nameAr.trim(), kind: form.kind, prefix: form.prefix.toUpperCase(), description: form.description.trim() || null, isActive: form.isActive };
    try {
      if (form.id) await accountingApi.updateEntryType(form.id, body); else await accountingApi.createEntryType(body);
      toast.success('تم حفظ نوع القيد');
      setForm(null); setError(''); onChanged();
    } catch (e: unknown) { setError(extractApiError(e, 'تعذر حفظ نوع القيد')); }
  }

  const locked = Boolean(form?.isSystem);

  return (
    <Dialog open={open} onClose={() => { setForm(null); onClose(); }} fullWidth maxWidth="md">
      <DialogTitle>أنواع القيود</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2}>
          {error ? <Alert severity="error">{error}</Alert> : null}
          <Table size="small">
            <TableHead><TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
              <TableCell>الاسم</TableCell><TableCell>الفئة</TableCell><TableCell>البادئة</TableCell><TableCell>الوصف</TableCell><TableCell>الحالة</TableCell><TableCell />
            </TableRow></TableHead>
            <TableBody>
              {types.map((t) => (
                <TableRow key={t.id} hover>
                  <TableCell sx={{ fontWeight: 700 }}>{t.nameAr}{t.isSystem ? <Chip size="small" variant="outlined" label="أساسي" sx={{ mx: 1 }} /> : null}</TableCell>
                  <TableCell>{KIND_LABEL[t.kind]}</TableCell><TableCell sx={{ fontFamily: 'monospace' }}>{t.prefix}</TableCell>
                  <TableCell>{t.description ?? ''}</TableCell>
                  <TableCell><Chip size="small" color={t.isActive ? 'success' : 'default'} label={t.isActive ? 'فعّال' : 'موقوف'} /></TableCell>
                  <TableCell>{canEdit ? <Button size="small" onClick={() => { setError(''); setForm({ id: t.id, nameAr: t.nameAr, kind: t.kind, prefix: t.prefix, description: t.description ?? '', isActive: t.isActive, isSystem: t.isSystem }); }}>تعديل</Button> : null}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>

          {form ? (
            <Stack spacing={2} sx={{ p: 2, border: 1, borderColor: 'divider', borderRadius: 2 }}>
              <Stack direction={{ xs: 'column', md: 'row' }} gap={2}>
                <TextField size="small" label="اسم النوع *" value={form.nameAr} onChange={(e) => setForm({ ...form, nameAr: e.target.value })} sx={{ flex: 2 }} />
                <TextField select size="small" label="الفئة" value={form.kind} disabled={locked} onChange={(e) => setForm({ ...form, kind: e.target.value as EntryKind })} sx={{ flex: 1 }}
                  helperText={locked ? 'الأنواع الأساسية لا تغيّر فئتها' : 'تحدد كيف يُسجَّل القيد في ERPNext'}>
                  {(Object.keys(KIND_LABEL) as EntryKind[]).map((k) => <MenuItem key={k} value={k}>{KIND_LABEL[k]}</MenuItem>)}
                </TextField>
                <TextField size="small" label="البادئة *" value={form.prefix} disabled={locked} onChange={(e) => setForm({ ...form, prefix: e.target.value.toUpperCase() })} inputProps={{ dir: 'ltr', maxLength: 6 }} sx={{ flex: 1 }} helperText="مثل EX → EX-2026-00001" />
              </Stack>
              <TextField size="small" label="الوصف" value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} />
              <FormControlLabel label="فعّال (يظهر عند إنشاء القيود)" control={<Switch checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />} />
              <Stack direction="row" gap={1}><Button variant="contained" onClick={() => void save()}>حفظ</Button><Button onClick={() => { setForm(null); setError(''); }}>إلغاء</Button></Stack>
            </Stack>
          ) : canEdit ? <Button sx={{ alignSelf: 'flex-start' }} onClick={() => { setError(''); setForm(EMPTY); }}>＋ نوع جديد</Button> : null}
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={() => { setForm(null); onClose(); }}>إغلاق</Button></DialogActions>
    </Dialog>
  );
}

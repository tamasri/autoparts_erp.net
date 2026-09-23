/** Warehouse control: every warehouse / shelf / vehicle with its stock, add and edit, deactivate (only when empty), and a jump to its item movements. */
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Alert, Autocomplete, Box, Button, Chip, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel,
  MenuItem, Paper, Stack, Switch, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography,
} from '@mui/material';
import { warehouseApi, type LocationOverview } from '../../api/endpoints/warehouse';
import { unwrapList } from '../../api/apiData';
import { extractApiError, toast } from '../../lib/toast';
import { invalidateLocations } from '../../hooks/useLocations';
import { num, type ExportDocument } from '../../lib/exportClient';
import PageHeader from '../../components/ui/PageHeader';
import ExportMenu from '../../components/ui/ExportMenu';
import Money from '../../components/ui/Money';

const TYPES: Array<{ value: string; label: string }> = [
  { value: 'WAREHOUSE', label: 'مستودع' }, { value: 'SHELF', label: 'رف / موقع' }, { value: 'VEHICLE', label: 'مركبة' },
  { value: 'RETURN', label: 'منطقة مرتجعات' }, { value: 'QUARANTINE', label: 'حجر / فحص' },
];
const typeLabel = (t: string): string => TYPES.find((x) => x.value === t)?.label ?? t;
const money = (v: number): string => v.toLocaleString('en-US', { maximumFractionDigits: 2 });

type Row = LocationOverview & { depth: number };

/** Parents first, children right under their parent, each with its depth for indentation. */
function order(list: LocationOverview[]): Row[] {
  const byParent = new Map<string | null, LocationOverview[]>();
  const ids = new Set(list.map((l) => l.id));
  for (const l of list) {
    const key = l.parentId && ids.has(l.parentId) ? l.parentId : null;
    byParent.set(key, [...(byParent.get(key) ?? []), l]);
  }
  const out: Row[] = [];
  const walk = (parent: string | null, depth: number): void => {
    for (const l of byParent.get(parent) ?? []) { out.push({ ...l, depth }); walk(l.id, depth + 1); }
  };
  walk(null, 0);
  return out;
}

type Form = { id?: string; code: string; name: string; type: string; parent: LocationOverview | null; isActive: boolean };
const emptyForm: Form = { code: '', name: '', type: 'WAREHOUSE', parent: null, isActive: true };

export default function Warehouses(): JSX.Element {
  const navigate = useNavigate();
  const [all, setAll] = useState<LocationOverview[]>([]);
  const [showInactive, setShowInactive] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [form, setForm] = useState<Form | null>(null);
  const [formError, setFormError] = useState('');
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    setLoading(true); setError('');
    try { setAll(unwrapList<LocationOverview>((await warehouseApi.overview(showInactive)).data)); }
    catch (e: unknown) { setError(extractApiError(e, 'تعذر تحميل المستودعات')); }
    finally { setLoading(false); }
  }, [showInactive]);

  useEffect(() => { void load(); }, [load]);

  const rows = useMemo(() => order(all), [all]);
  const totals = useMemo(() => all.filter((l) => l.isActive).reduce((a, l) => ({ qty: a.qty + l.totalQty, value: a.value + l.valueUsd }), { qty: 0, value: 0 }), [all]);
  const parentOptions = useMemo(() => all.filter((l) => l.isActive && l.id !== form?.id), [all, form?.id]);

  async function save(): Promise<void> {
    if (!form) return;
    if (!form.name.trim()) { setFormError('الاسم مطلوب'); return; }
    if (!form.id && !form.code.trim()) { setFormError('الرمز مطلوب'); return; }
    setSaving(true); setFormError('');
    try {
      if (form.id) await warehouseApi.update(form.id, { name: form.name, type: form.type, parentId: form.parent?.id ?? null, isActive: form.isActive });
      else await warehouseApi.create({ code: form.code, name: form.name, type: form.type, parentId: form.parent?.id ?? null });
      invalidateLocations();
      toast.success(form.id ? 'تم حفظ التعديلات' : 'تمت إضافة الموقع');
      setForm(null); await load();
    } catch (e: unknown) { setFormError(extractApiError(e, 'تعذر الحفظ')); }
    finally { setSaving(false); }
  }

  const buildExport = async (): Promise<ExportDocument> => ({
    title: 'المستودعات والمواقع', subtitle: 'الأرصدة الحالية لكل موقع', fileName: 'warehouses', fields: [{ label: 'إجمالي الكمية', value: money(totals.qty) }, { label: 'قيمة المخزون ($)', value: money(totals.value) }],
    tables: [{
      columns: ['الرمز', 'الاسم', 'النوع', 'الأصناف', 'الكمية', 'القيمة ($)', 'الحالة'],
      rows: rows.map((r) => [r.code, `${'    '.repeat(r.depth)}${r.name}`, typeLabel(r.type), num(r.skuCount), num(r.totalQty), num(r.valueUsd), r.isActive ? 'فعّال' : 'موقوف']),
      totals: ['', 'الإجمالي', '', '', num(totals.qty), num(totals.value), ''], numericColumns: [3, 4, 5],
    }],
  });

  return (
    <Box>
      <PageHeader
        title="المستودعات والمواقع"
        subtitle="تحكم كامل: الهيكل، الأرصدة، وحركة كل صنف"
        actions={(
          <>
            <FormControlLabel control={<Switch size="small" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />} label="إظهار الموقوفة" />
            <ExportMenu build={buildExport} disabled={loading} />
            <Button variant="contained" size="small" onClick={() => { setFormError(''); setForm({ ...emptyForm }); }}>＋ موقع جديد</Button>
          </>
        )}
      />
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      <Stack direction="row" gap={2} flexWrap="wrap" sx={{ mb: 2 }}>
        <Chip label={`مواقع فعّالة: ${all.filter((l) => l.isActive).length}`} />
        <Chip color="primary" variant="outlined" label={`إجمالي الكمية: ${money(totals.qty)}`} />
        <Chip color="success" variant="outlined" label={<>قيمة المخزون: <Money usd={totals.value} inline variant="body2" /></>} />
      </Stack>

      <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 3, opacity: loading ? 0.6 : 1 }}>
        <Table size="small">
          <TableHead>
            <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
              <TableCell>الموقع</TableCell><TableCell>النوع</TableCell><TableCell align="left">الأصناف</TableCell><TableCell align="left">الكمية</TableCell>
              <TableCell align="left">القيمة</TableCell><TableCell>الحالة</TableCell><TableCell />
            </TableRow>
          </TableHead>
          <TableBody>
            {loading && rows.length === 0 ? <TableRow><TableCell colSpan={7} align="center"><CircularProgress size={24} /></TableCell></TableRow> : null}
            {rows.map((r) => (
              <TableRow key={r.id} hover sx={{ opacity: r.isActive ? 1 : 0.55 }}>
                <TableCell sx={{ pr: 2 + r.depth * 3 }}>
                  <Typography component="span" fontWeight={r.depth === 0 ? 800 : 500}>{r.depth > 0 ? '└ ' : ''}{r.name}</Typography>
                  <Typography component="span" variant="caption" color="text.secondary" sx={{ mx: 1, fontFamily: 'monospace' }}>{r.code}</Typography>
                </TableCell>
                <TableCell><Chip size="small" variant="outlined" label={typeLabel(r.type)} /></TableCell>
                <TableCell align="left">{r.skuCount}</TableCell>
                <TableCell align="left" sx={{ fontWeight: 700 }}>{money(r.totalQty)}</TableCell>
                <TableCell align="left"><Money usd={r.valueUsd} /></TableCell>
                <TableCell><Chip size="small" color={r.isActive ? 'success' : 'default'} label={r.isActive ? 'فعّال' : 'موقوف'} /></TableCell>
                <TableCell align="left" sx={{ whiteSpace: 'nowrap' }}>
                  <Button size="small" onClick={() => navigate(`/inventory/movements?locationId=${r.id}`)}>حركة الأصناف</Button>
                  <Button size="small" onClick={() => { setFormError(''); setForm({ id: r.id, code: r.code, name: r.name, type: r.type, parent: all.find((l) => l.id === r.parentId) ?? null, isActive: r.isActive }); }}>تعديل</Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>

      <Dialog open={form !== null} onClose={() => setForm(null)} fullWidth maxWidth="xs">
        <DialogTitle>{form?.id ? 'تعديل موقع' : 'موقع جديد'}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            {formError ? <Alert severity="error">{formError}</Alert> : null}
            <TextField label="الرمز" value={form?.code ?? ''} disabled={Boolean(form?.id)} onChange={(e) => setForm((f) => (f ? { ...f, code: e.target.value } : f))} helperText={form?.id ? 'الرمز لا يتغير بعد الإنشاء' : 'أحرف إنجليزية وأرقام و - _'} inputProps={{ dir: 'ltr' }} />
            <TextField label="الاسم" value={form?.name ?? ''} onChange={(e) => setForm((f) => (f ? { ...f, name: e.target.value } : f))} />
            <TextField select label="النوع" value={form?.type ?? 'WAREHOUSE'} onChange={(e) => setForm((f) => (f ? { ...f, type: e.target.value } : f))}>
              {TYPES.map((t) => <MenuItem key={t.value} value={t.value}>{t.label}</MenuItem>)}
            </TextField>
            <Autocomplete
              options={parentOptions} value={form?.parent ?? null} onChange={(_, v) => setForm((f) => (f ? { ...f, parent: v } : f))}
              getOptionLabel={(o) => `${o.code} — ${o.name}`} isOptionEqualToValue={(a, b) => a.id === b.id} noOptionsText="لا توجد مواقع"
              renderInput={(p) => <TextField {...p} label="يتبع لـ (اختياري)" />}
            />
            {form?.id ? <FormControlLabel control={<Switch checked={form.isActive} onChange={(e) => setForm((f) => (f ? { ...f, isActive: e.target.checked } : f))} />} label="فعّال (لا يمكن إيقافه وفيه رصيد)" /> : null}
          </Stack>
        </DialogContent>
        <DialogActions><Button onClick={() => setForm(null)}>إلغاء</Button><Button variant="contained" disabled={saving} onClick={() => void save()}>حفظ</Button></DialogActions>
      </Dialog>
    </Box>
  );
}

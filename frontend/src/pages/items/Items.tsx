import { useState } from 'react';
import { Link as RouterLink, useNavigate } from 'react-router-dom';
import {
  Alert, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Stack, TextField, Typography,
} from '@mui/material';
import { itemsApi, type CreateItemBody } from '../../api/endpoints/items';
import { unwrapPaged } from '../../api/apiData';
import { usePagedList } from '../../hooks/usePagedList';
import { extractApiError, toast } from '../../lib/toast';
import { num, type ExportDocument } from '../../lib/exportClient';
import PageHeader from '../../components/ui/PageHeader';
import DataTable, { type Column } from '../../components/ui/DataTable';
import ExportMenu from '../../components/ui/ExportMenu';
import ItemImportDialog from '../../components/items/ItemImportDialog';

type ItemRow = {
  id: string; partNumber: string; nameEn: string; nameAr: string; brand?: string | null; isActive: boolean; isStopShip: boolean;
  hasWarranty: boolean; reorderLevel: number; availableQty: number;
};

const EMPTY: CreateItemBody = {
  partNumber: '', nameEn: '', nameAr: '', nameArColloquial: '', brand: '', categoryPath: '',
  hasWarranty: false, warrantyMonths: 0, isBatchTracked: false, reorderLevel: 0, notes: '',
};

function status(r: ItemRow): JSX.Element {
  if (!r.isActive) return <Chip size="small" variant="outlined" label="غير نشط" />;
  if (r.isStopShip) return <Chip size="small" color="warning" label="موقوف الشحن" />;
  if (Number(r.availableQty) <= Number(r.reorderLevel)) return <Chip size="small" color="warning" variant="outlined" label="تحت حد الطلب" />;
  return <Chip size="small" color="success" variant="outlined" label="نشط" />;
}

export default function Items(): JSX.Element {
  const navigate = useNavigate();
  const [includeInactive, setIncludeInactive] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const [form, setForm] = useState<CreateItemBody>(EMPTY);
  const [formError, setFormError] = useState('');
  const [saving, setSaving] = useState(false);

  const list = usePagedList<ItemRow>({
    errorMessage: 'تعذر تحميل الأصناف',
    deps: [includeInactive],
    fetcher: ({ page, pageSize, search }) => itemsApi.browse({ page, pageSize, search, includeInactive }),
  });

  const set = <K extends keyof CreateItemBody>(key: K, value: CreateItemBody[K]): void => setForm((f) => ({ ...f, [key]: value }));

  async function create(): Promise<void> {
    if (!form.partNumber.trim() || !form.nameEn.trim() || !form.nameAr.trim()) { setFormError('رقم القطعة والاسم بالعربية والإنجليزية مطلوبة'); return; }
    setSaving(true); setFormError('');
    try {
      const res = await itemsApi.create({
        ...form, partNumber: form.partNumber.trim(), nameArColloquial: form.nameArColloquial?.trim() || null,
        brand: form.brand?.trim() || null, categoryPath: form.categoryPath?.trim() || null, notes: form.notes?.trim() || null,
      });
      toast.success('تم إنشاء الصنف');
      setForm(EMPTY); setCreating(false);
      const id = (res.data as { data?: { id?: string } })?.data?.id;
      if (id) navigate(`/items/${id}`); else list.reload();
    } catch (e: unknown) { setFormError(extractApiError(e, 'تعذر إنشاء الصنف')); }
    finally { setSaving(false); }
  }

  // The API serves at most 100 rows per request, so export reads up to five pages of the current filter.
  const buildExport = async (): Promise<ExportDocument> => {
    const search = list.searchInput.trim() || undefined;
    const first = unwrapPaged<ItemRow>((await itemsApi.browse({ search, page: 1, pageSize: 100, includeInactive })).data);
    const items = [...first.items];
    for (let page = 2; page <= 5 && items.length < first.totalCount; page++) {
      items.push(...unwrapPaged<ItemRow>((await itemsApi.browse({ search, page, pageSize: 100, includeInactive })).data).items);
    }
    return {
      title: 'الأصناف', subtitle: `${first.totalCount} صنف${first.totalCount > 500 ? ' (أول 500)' : ''}`, fileName: 'items', fields: [],
      tables: [{
        columns: ['رقم القطعة', 'الاسم (عربي)', 'الاسم (EN)', 'العلامة', 'المتوفر', 'حد الطلب', 'الحالة'],
        rows: items.map((r) => [r.partNumber, r.nameAr, r.nameEn, r.brand ?? '', num(r.availableQty), num(r.reorderLevel), !r.isActive ? 'غير نشط' : r.isStopShip ? 'موقوف الشحن' : 'نشط']),
        numericColumns: [4, 5],
      }],
    };
  };

  const columns: Column<ItemRow>[] = [
    { header: 'رقم القطعة', render: (r) => <Button size="small" component={RouterLink} to={`/items/${r.id}`} sx={{ fontFamily: 'monospace', fontWeight: 700 }}>{r.partNumber}</Button> },
    { header: 'الاسم', render: (r) => <Box><Typography variant="body2" fontWeight={700}>{r.nameAr}</Typography><Typography variant="caption" color="text.secondary" sx={{ direction: 'ltr', display: 'block', textAlign: 'right' }}>{r.nameEn}</Typography></Box> },
    { header: 'العلامة', render: (r) => r.brand ?? '—' },
    { header: 'المتوفر', numeric: true, render: (r) => <strong style={{ color: Number(r.availableQty) > 0 ? undefined : 'crimson' }}>{Number(r.availableQty).toLocaleString('en-US')}</strong> },
    { header: 'حد الطلب', numeric: true, render: (r) => Number(r.reorderLevel).toLocaleString('en-US') },
    { header: 'الحالة', render: status },
  ];

  return (
    <>
      <PageHeader title="الأصناف" subtitle="بطاقات القطع: التفاصيل، المخزون، الأسماء البديلة، المكافئات والأسعار"
        actions={<><ExportMenu build={buildExport} /><Button variant="outlined" size="small" onClick={() => setImportOpen(true)}>⬆ استيراد Excel / CSV</Button><Button variant="contained" size="small" onClick={() => { setFormError(''); setCreating(true); }}>＋ صنف جديد</Button></>} />
      {list.error ? <Alert severity="error" sx={{ mb: 2 }}>{list.error}</Alert> : null}
      <Stack direction="row" gap={2} alignItems="center" flexWrap="wrap" sx={{ mb: 2 }}>
        <TextField size="small" placeholder="ابحث برقم القطعة أو الاسم أو العلامة..." value={list.searchInput} onChange={(e) => list.setSearchInput(e.target.value)} sx={{ width: 380 }} />
        <FormControlLabel label="عرض غير النشطة" control={<Checkbox size="small" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />} />
      </Stack>
      <DataTable
        columns={columns} rows={list.items} getKey={(r) => r.id} loading={list.loading} empty="لا توجد أصناف"
        paging={{ page: list.page - 1, pageSize: list.pageSize, total: list.totalCount, onPage: (p) => list.setPage(p + 1), onPageSize: list.changePageSize }}
      />

      <ItemImportDialog open={importOpen} onClose={() => setImportOpen(false)} onImported={() => list.reload()} />

      <Dialog open={creating} onClose={() => setCreating(false)} fullWidth maxWidth="sm">
        <DialogTitle>صنف جديد</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            {formError ? <Alert severity="error">{formError}</Alert> : null}
            <TextField size="small" label="رقم القطعة *" value={form.partNumber} onChange={(e) => set('partNumber', e.target.value)} inputProps={{ dir: 'ltr' }} />
            <Stack direction="row" gap={2}>
              <TextField size="small" fullWidth label="الاسم بالعربية *" value={form.nameAr} onChange={(e) => set('nameAr', e.target.value)} />
              <TextField size="small" fullWidth label="الاسم بالإنجليزية *" value={form.nameEn} onChange={(e) => set('nameEn', e.target.value)} inputProps={{ dir: 'ltr' }} />
            </Stack>
            <Stack direction="row" gap={2}>
              <TextField size="small" fullWidth label="الاسم الدارج" value={form.nameArColloquial ?? ''} onChange={(e) => set('nameArColloquial', e.target.value)} />
              <TextField size="small" fullWidth label="العلامة التجارية" value={form.brand ?? ''} onChange={(e) => set('brand', e.target.value)} />
            </Stack>
            <Stack direction="row" gap={2}>
              <TextField size="small" fullWidth type="number" label="حد إعادة الطلب" inputProps={{ min: 0 }} value={form.reorderLevel} onChange={(e) => set('reorderLevel', Number(e.target.value))} />
              <TextField size="small" fullWidth type="number" label="مدة الضمان (أشهر)" inputProps={{ min: 0 }} value={form.warrantyMonths} onChange={(e) => set('warrantyMonths', Number(e.target.value))} />
            </Stack>
            <Stack direction="row">
              <FormControlLabel label="ضمان" control={<Checkbox size="small" checked={form.hasWarranty} onChange={(e) => set('hasWarranty', e.target.checked)} />} />
              <FormControlLabel label="تتبع بالدفعات" control={<Checkbox size="small" checked={form.isBatchTracked} onChange={(e) => set('isBatchTracked', e.target.checked)} />} />
            </Stack>
          </Stack>
        </DialogContent>
        <DialogActions><Button onClick={() => setCreating(false)}>إلغاء</Button><Button variant="contained" disabled={saving} onClick={() => void create()}>حفظ الصنف</Button></DialogActions>
      </Dialog>
    </>
  );
}

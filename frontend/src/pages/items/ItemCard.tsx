/** One item: its data, stock by location, alternative names, interchangeable parts and selling prices. */
import { useCallback, useEffect, useState } from 'react';
import { Link as RouterLink, useParams } from 'react-router-dom';
import {
  Alert, Box, Button, Card, CardContent, Checkbox, Chip, CircularProgress, FormControlLabel, LinearProgress, Link, MenuItem, Stack, TextField, Typography,
} from '@mui/material';
import { itemsApi, type PriceBody, type UpdateItemBody } from '../../api/endpoints/items';
import { unwrapList, unwrapNode } from '../../api/apiData';
import { toast, extractApiError } from '../../lib/toast';
import { formatQty } from '../../lib/format';
import PageHeader from '../../components/ui/PageHeader';
import DataTable from '../../components/ui/DataTable';
import RoutedTabs from '../../components/ui/RoutedTabs';
import ReasonDialog from '../../components/ui/ReasonDialog';
import Money from '../../components/ui/Money';
import EntityPicker, { type PickerOption } from '../../components/pickers/EntityPicker';

type Item = {
  id: string; skuId?: string | null; partNumber: string; nameEn: string; nameAr: string;
  nameArColloquial?: string | null; brand?: string | null; categoryPath?: string | null;
  hasWarranty: boolean; warrantyMonths: number; isBatchTracked: boolean; reorderLevel: number;
  isActive: boolean; isStopShip: boolean; stopShipReason?: string | null; notes?: string | null;
};
type StockRow = { warehouse: string; availableQty: number; reservedQty: number };
type Alias = { id: string; alias: string; source: string; createdAt: string };
type Interchange = { id: string; interchangePartNumber: string; interchangeNameAr: string; type: string; priority: number; isActive: boolean };
type Sku = { sellingPriceSyp: number; sellingPriceUsd: number; minSellingPriceSyp: number; minSellingPriceUsd: number };
type Candidate = { id: string; partNumber: string; nameAr: string };

const INTERCHANGE_TYPES: Record<string, string> = { EQUIVALENT: 'مكافئ', SUPERSEDED: 'بديل أحدث', COMPATIBLE: 'متوافق' };

export default function ItemCard(): JSX.Element {
  const { id = '' } = useParams();
  const [item, setItem] = useState<Item | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadItem = useCallback(async (): Promise<void> => {
    try {
      setItem(unwrapNode<Item>((await itemsApi.getById(id)).data));
      setError('');
    } catch (e: unknown) {
      setError(extractApiError(e, 'تعذر تحميل بطاقة الصنف'));
    } finally { setLoading(false); }
  }, [id]);

  useEffect(() => { setLoading(true); void loadItem(); }, [loadItem]);

  if (loading) return <LinearProgress />;
  if (!item) return <Stack spacing={2}><Alert severity="error">{error || 'الصنف غير موجود'}</Alert><Link component={RouterLink} to="/items">← العودة للأصناف</Link></Stack>;

  return (
    <Box>
      <PageHeader
        title={item.nameAr}
        subtitle={`${item.partNumber} · ${item.nameEn}`}
        crumbs={[{ label: 'الأصناف', to: '/items' }, { label: item.partNumber }]}
        actions={(
          <Stack direction="row" gap={1}>
            {!item.isActive ? <Chip color="error" label="غير نشط" /> : null}
            {item.isStopShip ? <Chip color="warning" label="موقوف الشحن" /> : null}
          </Stack>
        )}
      />
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      <RoutedTabs tabs={[
        { key: 'overview', label: 'نظرة عامة', content: <Overview item={item} onChanged={loadItem} /> },
        { key: 'stock', label: 'المخزون', content: <StockTab id={item.id} reorderLevel={item.reorderLevel} /> },
        { key: 'aliases', label: 'الأسماء البديلة', content: <AliasesTab id={item.id} /> },
        { key: 'interchanges', label: 'المكافئات', content: <InterchangesTab id={item.id} /> },
        { key: 'prices', label: 'الأسعار', content: <PricesTab skuId={item.skuId ?? null} /> },
      ]} />
    </Box>
  );
}

function Overview({ item, onChanged }: { item: Item; onChanged: () => Promise<void> }): JSX.Element {
  const [form, setForm] = useState<UpdateItemBody>({
    nameEn: item.nameEn, nameAr: item.nameAr, nameArColloquial: item.nameArColloquial ?? '', brand: item.brand ?? '',
    categoryPath: item.categoryPath ?? '', isActive: item.isActive, hasWarranty: item.hasWarranty, warrantyMonths: item.warrantyMonths,
    isBatchTracked: item.isBatchTracked, reorderLevel: item.reorderLevel, notes: item.notes ?? '',
  });
  const [busy, setBusy] = useState(false);
  const [stopping, setStopping] = useState(false);
  const set = <K extends keyof UpdateItemBody>(k: K, v: UpdateItemBody[K]): void => setForm((f) => ({ ...f, [k]: v }));

  async function save(): Promise<void> {
    setBusy(true);
    try {
      await itemsApi.update(item.id, {
        ...form,
        nameArColloquial: form.nameArColloquial?.trim() || null,
        brand: form.brand?.trim() || null,
        categoryPath: form.categoryPath?.trim() || null,
        notes: form.notes?.trim() || null,
      });
      toast.success('تم حفظ التعديلات');
      await onChanged();
    } catch (e: unknown) { toast.error(extractApiError(e, 'تعذر حفظ التعديلات')); }
    finally { setBusy(false); }
  }

  async function stopShip(reason: string): Promise<void> {
    try { await itemsApi.stopShip(item.id, reason); toast.success('تم إيقاف شحن الصنف'); setStopping(false); await onChanged(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر إيقاف الشحن')); }
  }

  return (
    <Card variant="outlined" sx={{ borderRadius: 3 }}>
      <CardContent>
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 2, mb: 2 }}>
          <TextField size="small" label="رقم القطعة" value={item.partNumber} disabled />
          <TextField size="small" label="الاسم (EN)" value={form.nameEn} onChange={(e) => set('nameEn', e.target.value)} />
          <TextField size="small" label="الاسم (AR)" value={form.nameAr} onChange={(e) => set('nameAr', e.target.value)} />
          <TextField size="small" label="الاسم الدارج" value={form.nameArColloquial ?? ''} onChange={(e) => set('nameArColloquial', e.target.value)} />
          <TextField size="small" label="العلامة التجارية" value={form.brand ?? ''} onChange={(e) => set('brand', e.target.value)} />
          <TextField size="small" label="التصنيف" value={form.categoryPath ?? ''} onChange={(e) => set('categoryPath', e.target.value)} />
          <TextField size="small" type="number" label="حد إعادة الطلب" inputProps={{ min: 0 }} value={form.reorderLevel} onChange={(e) => set('reorderLevel', Number(e.target.value))} />
          <TextField size="small" type="number" label="مدة الضمان (أشهر)" inputProps={{ min: 0 }} value={form.warrantyMonths} onChange={(e) => set('warrantyMonths', Number(e.target.value))} />
        </Box>
        <TextField size="small" fullWidth multiline minRows={2} label="ملاحظات" value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value)} sx={{ mb: 1 }} />
        <Stack direction="row" gap={2} flexWrap="wrap" sx={{ mb: 1 }}>
          <FormControlLabel control={<Checkbox checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)} />} label="نشط" />
          <FormControlLabel control={<Checkbox checked={form.hasWarranty} onChange={(e) => set('hasWarranty', e.target.checked)} />} label="ضمان" />
          <FormControlLabel control={<Checkbox checked={form.isBatchTracked} onChange={(e) => set('isBatchTracked', e.target.checked)} />} label="تتبع بالدفعات" />
        </Stack>
        {item.isStopShip ? <Alert severity="warning" sx={{ mb: 2 }}>موقوف الشحن: {item.stopShipReason ?? '—'}</Alert> : null}
        <Stack direction="row" gap={1}>
          <Button variant="contained" disabled={busy} onClick={() => void save()}>{busy ? 'جارٍ الحفظ...' : 'حفظ التعديلات'}</Button>
          {!item.isStopShip ? <Button color="error" variant="outlined" onClick={() => setStopping(true)}>🚫 إيقاف الشحن</Button> : null}
        </Stack>
        <ReasonDialog open={stopping} title="سبب إيقاف الشحن" confirmLabel="إيقاف الشحن" onClose={() => setStopping(false)} onConfirm={stopShip} />
      </CardContent>
    </Card>
  );
}

function useTabData<T>(loader: () => Promise<{ data: unknown }>, errorMessage: string): { rows: T[]; loading: boolean; error: string; reload: () => void } {
  const [rows, setRows] = useState<T[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [tick, setTick] = useState(0);
  useEffect(() => {
    let live = true;
    setLoading(true);
    loader().then((res) => { if (live) { setRows(unwrapList<T>(res.data)); setError(''); } })
      .catch((e: unknown) => { if (live) setError(extractApiError(e, errorMessage)); })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tick]);
  return { rows, loading, error, reload: () => setTick((t) => t + 1) };
}

function StockTab({ id, reorderLevel }: { id: string; reorderLevel: number }): JSX.Element {
  const { rows, loading, error } = useTabData<StockRow>(() => itemsApi.getStock(id), 'تعذر تحميل المخزون');
  const total = rows.reduce((s, r) => s + Number(r.availableQty), 0);
  return (
    <Stack spacing={1.5}>
      {error ? <Alert severity="error">{error}</Alert> : null}
      <DataTable
        rows={rows} getKey={(r) => r.warehouse} loading={loading} empty="لا يوجد مخزون لهذا الصنف"
        columns={[
          { header: 'الموقع', render: (r) => <Typography fontWeight={600}>{r.warehouse}</Typography> },
          { header: 'المتوفر', render: (r) => <Typography fontWeight={700} color={Number(r.availableQty) > 0 ? 'success.main' : 'error.main'}>{formatQty(r.availableQty)}</Typography>, numeric: true },
          { header: 'المحجوز', render: (r) => formatQty(r.reservedQty), numeric: true },
        ]}
      />
      <Stack direction="row" gap={1} alignItems="center">
        <Typography variant="body2">الإجمالي المتوفر: <b>{formatQty(total)}</b> · حد إعادة الطلب: {formatQty(reorderLevel)}</Typography>
        {total <= reorderLevel ? <Chip size="small" color="warning" label="تحت حد الطلب" /> : null}
      </Stack>
    </Stack>
  );
}

function AliasesTab({ id }: { id: string }): JSX.Element {
  const { rows, loading, error, reload } = useTabData<Alias>(() => itemsApi.getAliases(id), 'تعذر تحميل الأسماء البديلة');
  const [alias, setAlias] = useState('');
  async function add(): Promise<void> {
    if (!alias.trim()) return;
    try { await itemsApi.addAlias(id, alias.trim()); setAlias(''); toast.success('تمت إضافة الاسم البديل'); reload(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر إضافة الاسم البديل')); }
  }
  return (
    <Card variant="outlined" sx={{ borderRadius: 3 }}>
      <CardContent>
        {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
        <Stack direction="row" gap={1} sx={{ mb: 2 }}>
          <TextField size="small" sx={{ maxWidth: 360, flex: 1 }} value={alias} onChange={(e) => setAlias(e.target.value)} placeholder="رقم أو اسم بديل للقطعة"
            onKeyDown={(e) => { if (e.key === 'Enter') void add(); }} />
          <Button variant="contained" onClick={() => void add()}>＋ إضافة</Button>
        </Stack>
        {loading ? <CircularProgress size={20} /> : rows.length === 0 ? <Typography color="text.secondary">لا توجد أسماء بديلة</Typography> : (
          <Stack direction="row" gap={1} flexWrap="wrap">
            {rows.map((a) => <Chip key={a.id} variant="outlined" label={a.alias} title={a.source} />)}
          </Stack>
        )}
      </CardContent>
    </Card>
  );
}

function InterchangesTab({ id }: { id: string }): JSX.Element {
  const { rows, loading, error, reload } = useTabData<Interchange>(() => itemsApi.getInterchanges(id), 'تعذر تحميل المكافئات');
  const [picked, setPicked] = useState<PickerOption | null>(null);
  const [type, setType] = useState('EQUIVALENT');

  const search = useCallback(async (text: string): Promise<PickerOption[]> => {
    if (!text) return [];
    const found = unwrapList<Candidate>((await itemsApi.search(text)).data).filter((c) => c.id !== id);
    return found.map((c) => ({ id: c.id, label: `${c.partNumber} — ${c.nameAr}` }));
  }, [id]);

  async function add(): Promise<void> {
    if (!picked) return;
    try { await itemsApi.addInterchange(id, picked.id, type, 1); setPicked(null); toast.success('تمت إضافة المكافئ'); reload(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر إضافة المكافئ')); }
  }

  return (
    <Stack spacing={2}>
      {error ? <Alert severity="error">{error}</Alert> : null}
      <Stack direction="row" gap={1} flexWrap="wrap" alignItems="flex-start">
        <Box sx={{ width: 340 }}><EntityPicker value={picked} onChange={setPicked} search={search} placeholder="ابحث عن صنف مكافئ..." /></Box>
        <TextField select size="small" value={type} onChange={(e) => setType(e.target.value)} sx={{ width: 160 }}>
          {Object.entries(INTERCHANGE_TYPES).map(([k, v]) => <MenuItem key={k} value={k}>{v}</MenuItem>)}
        </TextField>
        <Button variant="contained" disabled={!picked} onClick={() => void add()}>＋ إضافة</Button>
      </Stack>
      <DataTable
        rows={rows} getKey={(r) => r.id} loading={loading} empty="لا توجد مكافئات"
        columns={[
          { header: 'رقم القطعة', render: (r) => <Typography sx={{ fontFamily: 'monospace', fontWeight: 700 }}>{r.interchangePartNumber}</Typography> },
          { header: 'الاسم', render: (r) => r.interchangeNameAr },
          { header: 'النوع', render: (r) => INTERCHANGE_TYPES[r.type] ?? r.type },
          { header: 'الأولوية', render: (r) => r.priority, numeric: true },
        ]}
      />
    </Stack>
  );
}

function PricesTab({ skuId }: { skuId: string | null }): JSX.Element {
  const [sku, setSku] = useState<Sku | null>(null);
  const [form, setForm] = useState<PriceBody | null>(null);
  const [loading, setLoading] = useState(Boolean(skuId));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!skuId) return;
    itemsApi.getSku(skuId).then((r) => {
      const s = unwrapNode<Sku>(r.data);
      setSku(s);
      if (s) setForm({ sellingPriceSyp: s.sellingPriceSyp, sellingPriceUsd: s.sellingPriceUsd, minSellingPriceSyp: s.minSellingPriceSyp, minSellingPriceUsd: s.minSellingPriceUsd });
    }).catch((e: unknown) => setError(extractApiError(e, 'تعذر تحميل الأسعار'))).finally(() => setLoading(false));
  }, [skuId]);

  if (!skuId) return <Alert severity="info">هذا الصنف غير مرتبط بسجل تسعير (SKU).</Alert>;
  if (loading) return <LinearProgress />;
  if (!form || !sku) return <Alert severity="error">{error || 'تعذر تحميل الأسعار'}</Alert>;

  const set = (k: keyof PriceBody, v: number | string): void => setForm({ ...form, [k]: v });
  const belowMin = form.sellingPriceSyp < form.minSellingPriceSyp || form.sellingPriceUsd < form.minSellingPriceUsd;

  async function save(): Promise<void> {
    if (!form) return;
    if (belowMin && !form.overrideReason?.trim()) { toast.error('السعر أقل من الحد الأدنى: اكتب سبب التجاوز'); return; }
    setBusy(true);
    try { await itemsApi.updatePrices(skuId as string, form); toast.success('تم تحديث الأسعار'); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر تحديث الأسعار')); }
    finally { setBusy(false); }
  }

  return (
    <Card variant="outlined" sx={{ borderRadius: 3 }}>
      <CardContent>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>السعر الحالي: <Money usd={sku.sellingPriceUsd} syp={sku.sellingPriceSyp} inline /></Typography>
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 2, mb: 2 }}>
          <TextField size="small" type="number" label="سعر البيع (ل.س)" inputProps={{ min: 0 }} value={form.sellingPriceSyp} onChange={(e) => set('sellingPriceSyp', Number(e.target.value))} />
          <TextField size="small" type="number" label="سعر البيع ($)" inputProps={{ min: 0 }} value={form.sellingPriceUsd} onChange={(e) => set('sellingPriceUsd', Number(e.target.value))} />
          <TextField size="small" type="number" label="الحد الأدنى (ل.س)" inputProps={{ min: 0 }} value={form.minSellingPriceSyp} onChange={(e) => set('minSellingPriceSyp', Number(e.target.value))} />
          <TextField size="small" type="number" label="الحد الأدنى ($)" inputProps={{ min: 0 }} value={form.minSellingPriceUsd} onChange={(e) => set('minSellingPriceUsd', Number(e.target.value))} />
        </Box>
        {belowMin ? <TextField size="small" fullWidth label="سبب تجاوز الحد الأدنى *" value={form.overrideReason ?? ''} onChange={(e) => set('overrideReason', e.target.value)} sx={{ mb: 2 }} /> : null}
        <Button variant="contained" disabled={busy} onClick={() => void save()}>{busy ? 'جارٍ الحفظ...' : 'حفظ الأسعار'}</Button>
      </CardContent>
    </Card>
  );
}

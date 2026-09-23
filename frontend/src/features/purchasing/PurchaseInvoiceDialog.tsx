/** New supplier bill: supplier, receiving warehouse, dates and lines (item from the picker; quantity, cost and discount typed). */
import { useCallback, useMemo, useState } from 'react';
import {
  Alert, Autocomplete, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle, IconButton, Stack, Table, TableBody, TableCell,
  TableHead, TableRow, TextField, Typography,
} from '@mui/material';
import { purchasingApi, type PurchaseLineInput } from '../../api/endpoints/purchasing';
import { partiesApi } from '../../api/endpoints/parties';
import { unwrapPaged } from '../../api/apiData';
import { useLocations } from '../../hooks/useLocations';
import { extractApiError, toast } from '../../lib/toast';
import EntityPicker, { type PickerOption } from '../../components/pickers/EntityPicker';
import ItemPickerModal, { type PickedLine } from '../../components/pickers/ItemPickerModal';

type Line = PurchaseLineInput & { key: string; code: string; name: string };
type PartyRow = { id: string; displayNameAr?: string; displayName?: string; code?: string };

const today = (): string => new Date().toLocaleDateString('en-CA');
const lineTotal = (l: Line): number => l.quantity * l.unitCostUsd * (1 - l.discountPct / 100);
const money = (v: number): string => v.toLocaleString('en-US', { maximumFractionDigits: 2 });

export default function PurchaseInvoiceDialog({ open, onClose, onSaved }: { open: boolean; onClose: () => void; onSaved: () => void }): JSX.Element {
  const { locations } = useLocations('WAREHOUSE');
  const [supplier, setSupplier] = useState<PickerOption | null>(null);
  const [warehouseId, setWarehouseId] = useState('');
  const [billDate, setBillDate] = useState(today());
  const [dueDate, setDueDate] = useState(today());
  const [supplierRef, setSupplierRef] = useState('');
  const [notes, setNotes] = useState('');
  const [lines, setLines] = useState<Line[]>([]);
  const [discountMode, setDiscountMode] = useState<'pct' | 'usd'>('pct');
  const [discountValue, setDiscountValue] = useState(0);
  const [picking, setPicking] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const searchSuppliers = useCallback(async (text: string): Promise<PickerOption[]> => {
    const res = await partiesApi.getParties({ page: 1, pageSize: 10, typeCode: 'VENDOR', isActive: true, searchTerm: text || undefined });
    return unwrapPaged<PartyRow>(res.data).items.map((p) => ({ id: p.id, label: p.displayNameAr || p.displayName || p.id.slice(0, 8), sublabel: p.code }));
  }, []);

  const subtotal = useMemo(() => lines.reduce((a, l) => a + lineTotal(l), 0), [lines]);
  // The bill discount lowers the cost of every item on it proportionally when the bill is posted.
  const discount = discountMode === 'pct' ? subtotal * Math.min(Math.max(discountValue, 0), 100) / 100 : Math.max(discountValue, 0);
  const total = subtotal - discount;
  const patch = (key: string, change: Partial<Line>): void => setLines((all) => all.map((l) => (l.key === key ? { ...l, ...change } : l)));

  function addPicked(p: PickedLine): void {
    if (!p.itemId) return;
    setLines((all) => (all.some((l) => l.itemId === p.itemId)
      ? all
      : [...all, { key: crypto.randomUUID(), itemId: p.itemId as string, code: p.code, name: p.nameAr || p.name, quantity: 1, unitCostUsd: 0, discountPct: 0 }]));
  }

  function reset(): void {
    setSupplier(null); setWarehouseId(''); setBillDate(today()); setDueDate(today()); setSupplierRef(''); setNotes(''); setLines([]); setDiscountValue(0); setError('');
  }

  async function save(): Promise<void> {
    if (!supplier) { setError('اختر المورّد'); return; }
    if (!warehouseId) { setError('اختر المستودع المستلِم'); return; }
    if (lines.length === 0) { setError('أضف صنفاً واحداً على الأقل'); return; }
    if (lines.some((l) => !(l.quantity > 0) || l.unitCostUsd < 0)) { setError('تحقق من الكميات والتكاليف'); return; }
    if (discountMode === 'pct' ? discountValue > 100 : discountValue > subtotal) { setError('خصم الفاتورة أكبر من مجموع البنود'); return; }
    setSaving(true); setError('');
    try {
      await purchasingApi.createInvoice({
        supplierPartyId: supplier.id, billDate, dueDate, warehouseId, supplierRef: supplierRef.trim() || undefined, notes: notes.trim() || undefined,
        lines: lines.map(({ itemId, quantity, unitCostUsd, discountPct }) => ({ itemId, quantity, unitCostUsd, discountPct })),
        discountPct: discountMode === 'pct' && discountValue > 0 ? discountValue : undefined,
        discountAmountUsd: discountMode === 'usd' && discountValue > 0 ? discountValue : undefined,
      });
      toast.success('تم حفظ فاتورة الشراء كمسودة');
      reset(); onSaved(); onClose();
    } catch (e: unknown) {
      setError(extractApiError(e, 'تعذر حفظ فاتورة الشراء'));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="lg">
      <DialogTitle>فاتورة شراء جديدة</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2}>
          {error ? <Alert severity="error">{error}</Alert> : null}
          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(230px, 1fr))', gap: 2 }}>
            <Box><Typography variant="caption" color="text.secondary">المورّد *</Typography><EntityPicker value={supplier} onChange={setSupplier} search={searchSuppliers} placeholder="ابحث باسم المورّد..." /></Box>
            <Autocomplete
              options={locations} value={locations.find((l) => l.id === warehouseId) ?? null} onChange={(_, v) => setWarehouseId(v?.id ?? '')}
              getOptionLabel={(o) => `${o.code} — ${o.name}`} isOptionEqualToValue={(a, b) => a.id === b.id} renderInput={(p) => <TextField {...p} label="المستودع المستلِم *" size="small" />}
            />
            <TextField size="small" type="date" label="تاريخ الفاتورة" value={billDate} onChange={(e) => setBillDate(e.target.value)} InputLabelProps={{ shrink: true }} />
            <TextField size="small" type="date" label="تاريخ الاستحقاق" value={dueDate} onChange={(e) => setDueDate(e.target.value)} InputLabelProps={{ shrink: true }} />
            <TextField size="small" label="رقم فاتورة المورّد" value={supplierRef} onChange={(e) => setSupplierRef(e.target.value)} />
            <TextField size="small" label="ملاحظات" value={notes} onChange={(e) => setNotes(e.target.value)} />
          </Box>

          <Table size="small">
            <TableHead>
              <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
                <TableCell>الصنف</TableCell><TableCell width={110}>الكمية</TableCell><TableCell width={130}>تكلفة الوحدة ($)</TableCell>
                <TableCell width={100}>خصم %</TableCell><TableCell width={120} align="left">الإجمالي ($)</TableCell><TableCell width={50} />
              </TableRow>
            </TableHead>
            <TableBody>
              {lines.length === 0 ? <TableRow><TableCell colSpan={6} align="center" sx={{ py: 3, color: 'text.secondary' }}>لا توجد أصناف بعد</TableCell></TableRow> : null}
              {lines.map((l) => (
                <TableRow key={l.key}>
                  <TableCell><Typography component="span" sx={{ fontFamily: 'monospace', fontWeight: 700 }}>{l.code}</Typography> {l.name}</TableCell>
                  <TableCell><TextField size="small" type="number" inputProps={{ min: 0 }} value={l.quantity} onChange={(e) => patch(l.key, { quantity: Number(e.target.value) })} /></TableCell>
                  <TableCell><TextField size="small" type="number" inputProps={{ min: 0, step: '0.01' }} value={l.unitCostUsd} onChange={(e) => patch(l.key, { unitCostUsd: Number(e.target.value) })} /></TableCell>
                  <TableCell><TextField size="small" type="number" inputProps={{ min: 0, max: 100 }} value={l.discountPct} onChange={(e) => patch(l.key, { discountPct: Number(e.target.value) })} /></TableCell>
                  <TableCell align="left" sx={{ fontWeight: 700 }}>{money(lineTotal(l))}</TableCell>
                  <TableCell><IconButton size="small" onClick={() => setLines((all) => all.filter((x) => x.key !== l.key))}>✕</IconButton></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Stack direction="row" justifyContent="space-between" alignItems="center" flexWrap="wrap" gap={2}>
            <Button variant="outlined" onClick={() => setPicking(true)}>＋ إضافة أصناف</Button>
            <Stack direction="row" spacing={1} alignItems="center">
              <TextField select size="small" label="خصم على الفاتورة" value={discountMode} onChange={(e) => setDiscountMode(e.target.value as 'pct' | 'usd')} SelectProps={{ native: true }} sx={{ width: 150 }}>
                <option value="pct">نسبة %</option>
                <option value="usd">مبلغ $</option>
              </TextField>
              <TextField size="small" type="number" inputProps={{ min: 0, max: discountMode === 'pct' ? 100 : undefined, step: '0.01' }} value={discountValue} onChange={(e) => setDiscountValue(Number(e.target.value))} sx={{ width: 110 }} />
            </Stack>
            <Box textAlign="left">
              {discount > 0 ? <Typography variant="body2" color="text.secondary">مجموع البنود ${money(subtotal)} − خصم ${money(discount)}</Typography> : null}
              <Typography variant="h6" fontWeight={800}>الإجمالي: ${money(total)}</Typography>
            </Box>
          </Stack>
        </Stack>
        <ItemPickerModal open={picking} mode="warehouse" title="اختيار الأصناف المشتراة" initialLocationId={warehouseId} onPick={addPicked} onClose={() => setPicking(false)} />
      </DialogContent>
      <DialogActions>
        <Button onClick={() => { reset(); onClose(); }}>إلغاء</Button>
        <Button variant="contained" disabled={saving} onClick={() => void save()}>حفظ كمسودة</Button>
      </DialogActions>
    </Dialog>
  );
}

import { useEffect, useMemo, useRef, useState } from 'react';
import { lookupsApi, type PickItem } from '../../api/endpoints/lookups';
import { usePagedList } from '../../hooks/usePagedList';
import { useLocations } from '../../hooks/useLocations';
import {
  Alert, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Stack, Table, TableBody, TableCell,
  TableContainer, TableHead, TablePagination, TableRow, TextField, Typography,
} from '@mui/material';
import { ARABIC_PAGINATION } from '../../lib/tablePagination';
import { formatQty } from '../../lib/format';
import Money from '../ui/Money';
import LocationSelect from './LocationSelect';

/** What the dialog hands back for one chosen item — everything a document line needs, already resolved. */
export type PickedLine = {
  itemId?: string | null;
  skuId?: string | null;
  code: string;
  name: string;
  nameAr: string;
  locationId: string;
  locationCode: string;
  batchId?: string;
  batchNumber?: string;
  quantity: number;
  unitPriceSyp: number;
  unitPriceUsd: number;
  minPriceSyp: number;
  minPriceUsd: number;
  available: number;
  isBatchTracked: boolean;
  /** Full catalogue row, kept so the line can offer other locations/batches later without another request. */
  item: PickItem;
};

type Props = {
  open: boolean;
  /** `sales`: SKUs + invoicing stock + prices. `warehouse`: items (incl. never-stocked) + warehouse balances. */
  mode: 'sales' | 'warehouse';
  title?: string;
  initialLocationId?: string;
  /** Pre-fills the search box (used when a scanned code did not resolve to a single item). */
  initialSearch?: string;
  /** When false (sales returns) stock limits are not enforced and any location can be chosen. Default true. */
  enforceStock?: boolean;
  /** Called for every item the user adds; the dialog stays open so many items can be added in one go. */
  onPick: (line: PickedLine) => void;
  onClose: () => void;
};

type RowState = { locationId: string; batchId: string; qty: string };

const rowKey = (it: PickItem): string => it.skuId ?? it.itemId ?? it.code;

export default function ItemPickerModal({ open, mode, title, initialLocationId = '', initialSearch = '', enforceStock = true, onPick, onClose }: Props): JSX.Element {
  const [locationFilter, setLocationFilter] = useState(initialLocationId);
  const [inStockOnly, setInStockOnly] = useState(mode === 'sales' && enforceStock);
  const pickAnyLocation = mode === 'warehouse' || !enforceStock;
  const [rowStates, setRowStates] = useState<Record<string, RowState>>({});
  const [active, setActive] = useState(0);
  const [added, setAdded] = useState<string[]>([]);
  const searchRef = useRef<HTMLInputElement>(null);
  const pendingEnter = useRef(false);
  const { locations } = useLocations('');

  const list = usePagedList<PickItem>({
    errorMessage: 'تعذر تحميل الأصناف',
    pageSize: 10,
    deps: [mode, locationFilter, inStockOnly, open],
    fetcher: ({ page, pageSize, search }) =>
      open
        ? lookupsApi.pickItems({ search, mode, locationId: locationFilter || undefined, inStockOnly, page, pageSize })
        : Promise.resolve({ data: { items: [], totalCount: 0 } }),
  });

  useEffect(() => {
    if (open) {
      setAdded([]);
      setActive(0);
      list.setSearchInput(initialSearch);
      window.setTimeout(() => searchRef.current?.focus(), 30);
    }
  }, [open]);

  useEffect(() => setActive(0), [list.items]);

  // Barcode scanners type the code and press Enter before the debounced search has run; remember the Enter and
  // apply it as soon as the (single) result arrives.
  useEffect(() => {
    if (pendingEnter.current && !list.loading && list.search === list.searchInput.trim()) {
      pendingEnter.current = false;
      if (list.items.length === 1) addRow(list.items[0]);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [list.loading, list.items, list.search]);

  const stateFor = (it: PickItem): RowState => {
    const existing = rowStates[rowKey(it)];
    if (existing) return existing;
    const preferred = locationFilter
      ? it.stock.find((s) => s.locationId === locationFilter)
      : [...it.stock].sort((a, b) => b.available - a.available)[0];
    const locationId = pickAnyLocation ? (locationFilter || preferred?.locationId || '') : (preferred?.locationId ?? '');
    const batch = it.isBatchTracked ? it.batches.find((b) => b.locationId === locationId) : undefined;
    return { locationId, batchId: batch?.id ?? '', qty: '1' };
  };

  function patchRow(it: PickItem, patch: Partial<RowState>): void {
    setRowStates((prev) => {
      const current = prev[rowKey(it)] ?? stateFor(it);
      const next = { ...current, ...patch };
      // Changing location invalidates the batch; re-pick the first batch (FEFO order) at the new location.
      if (patch.locationId !== undefined && patch.locationId !== current.locationId) {
        next.batchId = it.isBatchTracked ? (it.batches.find((b) => b.locationId === patch.locationId)?.id ?? '') : '';
      }
      return { ...prev, [rowKey(it)]: next };
    });
  }

  function availableAt(it: PickItem, st: RowState): number {
    if (it.isBatchTracked && st.batchId) return it.batches.find((b) => b.id === st.batchId)?.quantity ?? 0;
    return it.stock.find((s) => s.locationId === st.locationId)?.available ?? 0;
  }

  function blockReason(it: PickItem, st: RowState): string {
    const qty = Number(st.qty);
    if (it.isStopShip) return 'موقوف الشحن';
    if (mode === 'sales' && !it.skuId) return 'غير مربوط بسجل تسعير';
    if (!st.locationId) return pickAnyLocation ? 'اختر الموقع' : 'لا يوجد مخزون متاح';
    if (!(qty > 0)) return 'أدخل كمية صحيحة';
    if (it.isBatchTracked && mode === 'sales' && enforceStock && !st.batchId) return 'اختر الدفعة';
    if (mode === 'sales' && enforceStock && qty > availableAt(it, st)) return `الكمية تتجاوز المتاح (${formatQty(availableAt(it, st))})`;
    return '';
  }

  function addRow(it: PickItem): void {
    const st = stateFor(it);
    if (blockReason(it, st)) return;
    const loc = locations.find((l) => l.id === st.locationId);
    const batch = it.batches.find((b) => b.id === st.batchId);
    onPick({
      itemId: it.itemId,
      skuId: it.skuId,
      code: it.code,
      name: it.name,
      nameAr: it.nameAr,
      locationId: st.locationId,
      locationCode: it.stock.find((s) => s.locationId === st.locationId)?.locationCode ?? loc?.code ?? '',
      batchId: st.batchId || undefined,
      batchNumber: batch?.batchNumber,
      quantity: Number(st.qty),
      unitPriceSyp: it.sellingPriceSyp,
      unitPriceUsd: it.sellingPriceUsd,
      minPriceSyp: it.minSellingPriceSyp,
      minPriceUsd: it.minSellingPriceUsd,
      available: availableAt(it, st),
      isBatchTracked: it.isBatchTracked,
      item: it,
    });
    setAdded((prev) => [...prev, it.code]);
    setRowStates((prev) => ({ ...prev, [rowKey(it)]: { ...st, qty: '1' } }));
    searchRef.current?.select();
  }

  const stats = useMemo(() => ({ count: added.length }), [added]);

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="lg" aria-label={title ?? 'اختيار الأصناف'}>
      <DialogTitle>{title ?? (mode === 'sales' ? 'اختيار الأصناف للفاتورة' : 'اختيار الأصناف')}</DialogTitle>
      <DialogContent dividers>
        <Stack direction="row" gap={1.5} flexWrap="wrap" alignItems="center" sx={{ mb: 1.5 }}>
          <TextField
            inputRef={searchRef}
            size="small"
            sx={{ flex: '1 1 320px' }}
            value={list.searchInput}
            placeholder="ابحث بالاسم أو رقم القطعة أو الباركود أو الاسم البديل… (امسح الباركود ثم Enter)"
            onChange={(e) => list.setSearchInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'ArrowDown') { e.preventDefault(); setActive((a) => Math.min(a + 1, list.items.length - 1)); }
              else if (e.key === 'ArrowUp') { e.preventDefault(); setActive((a) => Math.max(a - 1, 0)); }
              else if (e.key === 'Enter') {
                e.preventDefault();
                if (list.search !== list.searchInput.trim() || list.loading) pendingEnter.current = true;
                else if (list.items[active]) addRow(list.items[active]);
              }
            }}
          />
          <Box sx={{ minWidth: 220 }}><LocationSelect value={locationFilter} onChange={(id) => setLocationFilter(id)} placeholder="كل المواقع" /></Box>
          <FormControlLabel control={<Checkbox size="small" checked={inStockOnly} onChange={(e) => setInStockOnly(e.target.checked)} />} label="المتوفر فقط" />
        </Stack>

        {list.error ? <Alert severity="error" sx={{ mb: 1 }}>{list.error}</Alert> : null}

        <TableContainer sx={{ opacity: list.loading ? 0.6 : 1, transition: 'opacity 120ms' }}>
          <Table size="small">
            <TableHead>
              <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
                <TableCell>الصنف</TableCell>
                {mode === 'sales' ? <TableCell>السعر</TableCell> : null}
                <TableCell>{mode === 'sales' ? 'الموقع (المتاح)' : 'الموقع'}</TableCell>
                {mode === 'sales' ? <TableCell>الدفعة</TableCell> : null}
                <TableCell sx={{ width: 100 }}>الكمية</TableCell>
                <TableCell sx={{ width: 130 }} />
              </TableRow>
            </TableHead>
            <TableBody>
              {list.items.length === 0 ? (
                <TableRow><TableCell colSpan={6} align="center" sx={{ py: 4, color: 'text.secondary' }}>{list.loading ? 'جارٍ البحث...' : 'لا توجد أصناف مطابقة'}</TableCell></TableRow>
              ) : list.items.map((it, idx) => {
                const st = stateFor(it);
                const reason = blockReason(it, st);
                const stockHere = it.stock.filter((s) => (pickAnyLocation ? true : s.available > 0));
                const batchesHere = it.batches.filter((b) => b.locationId === st.locationId);
                return (
                  <TableRow key={rowKey(it)} hover selected={idx === active} onMouseEnter={() => setActive(idx)}>
                    <TableCell>
                      <Typography sx={{ fontFamily: 'monospace', fontWeight: 700 }} color="primary">{it.code}</Typography>
                      <Typography variant="body2" fontWeight={600}>{it.nameAr}</Typography>
                      <Typography variant="caption" color="text.secondary" sx={{ direction: 'ltr', display: 'block', textAlign: 'right' }}>{it.name}{it.brand ? ` · ${it.brand}` : ''}</Typography>
                      <Stack direction="row" gap={0.5} flexWrap="wrap" sx={{ mt: 0.5 }}>
                        {it.isStopShip ? <Chip size="small" color="error" label="موقوف الشحن" /> : null}
                        {it.hasWarranty ? <Chip size="small" color="success" variant="outlined" label="ضمان" /> : null}
                        {it.isBatchTracked ? <Chip size="small" variant="outlined" label="دفعات" /> : null}
                        {mode === 'sales' && it.totalAvailable <= 0 ? <Chip size="small" color="warning" label="نافد" /> : null}
                        {it.barcode ? <Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'monospace' }}>{it.barcode}</Typography> : null}
                      </Stack>
                    </TableCell>
                    {mode === 'sales' ? (
                      <TableCell>
                        <Money usd={it.sellingPriceUsd} syp={it.sellingPriceSyp} fontWeight={700} />
                        {it.minSellingPriceUsd > 0 ? <Typography variant="caption" color="text.secondary" component="div">الأدنى <Money usd={it.minSellingPriceUsd} syp={it.minSellingPriceSyp} inline variant="caption" /></Typography> : null}
                      </TableCell>
                    ) : null}
                    <TableCell sx={{ minWidth: 170 }}>
                      {pickAnyLocation ? (
                        <LocationSelect value={st.locationId} onChange={(id) => patchRow(it, { locationId: id })} allowEmpty />
                      ) : stockHere.length === 0 ? (
                        <Typography variant="body2" color="text.secondary">لا مخزون</Typography>
                      ) : (
                        <TextField select size="small" fullWidth value={st.locationId} onChange={(e) => patchRow(it, { locationId: e.target.value })}>
                          {stockHere.map((s) => <MenuItem key={s.locationId} value={s.locationId}>{s.locationCode} ({formatQty(s.available)})</MenuItem>)}
                        </TextField>
                      )}
                    </TableCell>
                    {mode === 'sales' ? (
                      <TableCell sx={{ minWidth: 150 }}>
                        {it.isBatchTracked && batchesHere.length > 0 ? (
                          <TextField select size="small" fullWidth value={st.batchId} onChange={(e) => patchRow(it, { batchId: e.target.value })}>
                            {batchesHere.map((b) => <MenuItem key={b.id} value={b.id}>{b.batchNumber} ({formatQty(b.quantity)}){b.expiryDate ? ` · ${b.expiryDate}` : ''}</MenuItem>)}
                          </TextField>
                        ) : '—'}
                      </TableCell>
                    ) : null}
                    <TableCell>
                      <TextField
                        size="small" type="number" inputProps={{ min: 0 }} value={st.qty}
                        onChange={(e) => patchRow(it, { qty: e.target.value })}
                        onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); addRow(it); } }}
                      />
                    </TableCell>
                    <TableCell>
                      <Button variant="contained" size="small" fullWidth disabled={Boolean(reason)} title={reason} onClick={() => addRow(it)}>＋ إضافة</Button>
                      {reason ? <Typography variant="caption" color="error" component="div">{reason}</Typography> : null}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </TableContainer>
        <TablePagination
          component="div" count={list.totalCount} page={Math.max(0, list.page - 1)} rowsPerPage={list.pageSize} rowsPerPageOptions={[list.pageSize]}
          onPageChange={(_, p) => list.setPage(p + 1)} {...ARABIC_PAGINATION}
        />
      </DialogContent>
      <DialogActions sx={{ justifyContent: 'space-between', px: 3 }}>
        <Typography variant="body2" color="text.secondary">
          {stats.count > 0 ? <>✓ أُضيف {stats.count} — <span style={{ fontFamily: 'monospace' }}>{added.slice(-3).join('، ')}</span></> : 'اختر الصنف والموقع والكمية ثم اضغط إضافة — تبقى النافذة مفتوحة لإضافة المزيد. ↑↓ للتنقل، Enter للإضافة، Esc للإغلاق.'}
        </Typography>
        <Button variant="contained" onClick={onClose}>تم</Button>
      </DialogActions>
    </Dialog>
  );
}

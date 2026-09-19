import { useEffect, useMemo, useRef, useState } from 'react';
import { lookupsApi, type PickItem } from '../../api/endpoints/lookups';
import { usePagedList } from '../../hooks/usePagedList';
import { useLocations } from '../../hooks/useLocations';
import Pagination from '../common/Pagination';
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
const num = (v: number): string => Number(v ?? 0).toLocaleString('en-US');

export default function ItemPickerModal({ open, mode, title, initialLocationId = '', initialSearch = '', enforceStock = true, onPick, onClose }: Props): JSX.Element | null {
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

  useEffect(() => {
    if (!open) return undefined;
    const onKey = (e: KeyboardEvent): void => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [open, onClose]);

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
    if (mode === 'sales' && enforceStock && qty > availableAt(it, st)) return `الكمية تتجاوز المتاح (${num(availableAt(it, st))})`;
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

  if (!open) return null;

  return (
    <div className="vex-modal-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="vex-modal" role="dialog" aria-modal="true" aria-label={title ?? 'اختيار الأصناف'}>
        <div className="vex-modal__header">
          <h2 className="vex-section-title" style={{ margin: 0 }}>{title ?? (mode === 'sales' ? 'اختيار الأصناف للفاتورة' : 'اختيار الأصناف')}</h2>
          <button type="button" className="btn-ghost" onClick={onClose} aria-label="إغلاق">✕</button>
        </div>

        <div className="vex-modal__body">
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', alignItems: 'center', marginBottom: 14 }}>
            <input
              ref={searchRef}
              className="vex-input"
              style={{ flex: '1 1 320px' }}
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
            <div style={{ minWidth: 220 }}>
              <LocationSelect value={locationFilter} onChange={(id) => setLocationFilter(id)} placeholder="كل المواقع" />
            </div>
            <label style={{ fontSize: 13, color: 'var(--txt-secondary)', whiteSpace: 'nowrap' }}>
              <input type="checkbox" checked={inStockOnly} onChange={(e) => setInStockOnly(e.target.checked)} /> المتوفر فقط
            </label>
          </div>

          {list.error ? <div className="badge badge--danger" style={{ marginBottom: 10 }}>{list.error}</div> : null}

          <div style={{ overflowX: 'auto', opacity: list.loading ? 0.6 : 1, transition: 'opacity 120ms' }}>
            <table className="vex-table">
              <thead>
                <tr>
                  <th>الصنف</th>
                  {mode === 'sales' ? <th>السعر</th> : null}
                  <th>{mode === 'sales' ? 'الموقع (المتاح)' : 'الموقع'}</th>
                  {mode === 'sales' ? <th>الدفعة</th> : null}
                  <th style={{ width: 90 }}>الكمية</th>
                  <th style={{ width: 120 }}></th>
                </tr>
              </thead>
              <tbody>
                {list.items.length === 0 ? (
                  <tr><td colSpan={6} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '28px 0' }}>{list.loading ? 'جارٍ البحث...' : 'لا توجد أصناف مطابقة'}</td></tr>
                ) : list.items.map((it, idx) => {
                  const st = stateFor(it);
                  const reason = blockReason(it, st);
                  const stockHere = it.stock.filter((s) => (pickAnyLocation ? true : s.available > 0));
                  const batchesHere = it.batches.filter((b) => b.locationId === st.locationId);
                  return (
                    <tr key={rowKey(it)} className={idx === active ? 'vex-row--active' : undefined} onMouseEnter={() => setActive(idx)}>
                      <td>
                        <div style={{ fontFamily: 'monospace', fontWeight: 700, color: 'var(--clr-primary)' }}>{it.code}</div>
                        <div style={{ fontWeight: 600 }}>{it.nameAr}</div>
                        <div style={{ fontSize: 12, color: 'var(--txt-muted)', direction: 'ltr', textAlign: 'right' }}>{it.name}{it.brand ? ` · ${it.brand}` : ''}</div>
                        <div style={{ display: 'flex', gap: 4, marginTop: 4, flexWrap: 'wrap' }}>
                          {it.isStopShip ? <span className="badge badge--danger">موقوف الشحن</span> : null}
                          {it.hasWarranty ? <span className="badge badge--success">ضمان</span> : null}
                          {it.isBatchTracked ? <span className="badge badge--draft">دفعات</span> : null}
                          {mode === 'sales' && it.totalAvailable <= 0 ? <span className="badge badge--warning">نافد</span> : null}
                          {it.barcode ? <span style={{ fontSize: 11, color: 'var(--txt-muted)', fontFamily: 'monospace' }}>{it.barcode}</span> : null}
                        </div>
                      </td>
                      {mode === 'sales' ? (
                        <td style={{ whiteSpace: 'nowrap' }}>
                          <div style={{ fontWeight: 700 }}>${num(it.sellingPriceUsd)}</div>
                          <div style={{ fontSize: 12, color: 'var(--txt-secondary)' }}>{num(it.sellingPriceSyp)} ل.س</div>
                          {it.minSellingPriceUsd > 0 ? <div style={{ fontSize: 11, color: 'var(--txt-muted)' }}>الأدنى ${num(it.minSellingPriceUsd)}</div> : null}
                        </td>
                      ) : null}
                      <td>
                        {pickAnyLocation ? (
                          <LocationSelect value={st.locationId} onChange={(id) => patchRow(it, { locationId: id })} allowEmpty />
                        ) : stockHere.length === 0 ? (
                          <span style={{ color: 'var(--txt-muted)' }}>لا مخزون</span>
                        ) : (
                          <select className="vex-select" value={st.locationId} onChange={(e) => patchRow(it, { locationId: e.target.value })}>
                            {stockHere.map((s) => (
                              <option key={s.locationId} value={s.locationId}>{s.locationCode} ({num(s.available)})</option>
                            ))}
                          </select>
                        )}
                      </td>
                      {mode === 'sales' ? (
                        <td>
                          {it.isBatchTracked ? (
                            batchesHere.length === 0 ? <span style={{ color: 'var(--txt-muted)' }}>—</span> : (
                              <select className="vex-select" value={st.batchId} onChange={(e) => patchRow(it, { batchId: e.target.value })}>
                                {batchesHere.map((b) => (
                                  <option key={b.id} value={b.id}>{b.batchNumber} ({num(b.quantity)}){b.expiryDate ? ` · ${b.expiryDate}` : ''}</option>
                                ))}
                              </select>
                            )
                          ) : <span style={{ color: 'var(--txt-muted)' }}>—</span>}
                        </td>
                      ) : null}
                      <td>
                        <input
                          type="number"
                          min={0}
                          className="vex-input"
                          value={st.qty}
                          onChange={(e) => patchRow(it, { qty: e.target.value })}
                          onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); addRow(it); } }}
                        />
                      </td>
                      <td>
                        <button type="button" className="btn-primary" style={{ padding: '6px 14px', fontSize: 13, width: '100%' }} disabled={Boolean(reason)} title={reason} onClick={() => addRow(it)}>
                          ＋ إضافة
                        </button>
                        {reason ? <div style={{ fontSize: 11, color: 'var(--clr-danger)', marginTop: 3 }}>{reason}</div> : null}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <Pagination page={list.page} pageSize={list.pageSize} totalCount={list.totalCount} onPageChange={list.setPage} />
        </div>

        <div className="vex-modal__footer">
          <div style={{ fontSize: 13, color: 'var(--txt-secondary)' }}>
            {stats.count > 0 ? <>✓ أُضيف {stats.count} — <span style={{ fontFamily: 'monospace' }}>{added.slice(-3).join('، ')}</span></> : 'اختر الصنف والموقع والكمية ثم اضغط إضافة — تبقى النافذة مفتوحة لإضافة المزيد. ↑↓ للتنقل، Enter للإضافة، Esc للإغلاق.'}
          </div>
          <button type="button" className="btn-primary" onClick={onClose}>تم</button>
        </div>
      </div>
    </div>
  );
}

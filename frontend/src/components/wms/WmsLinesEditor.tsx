import { useCallback, useState } from 'react';
import type { PickItem } from '../../api/endpoints/lookups';
import ItemPickerModal, { type PickedLine } from '../pickers/ItemPickerModal';
import LocationSelect from '../pickers/LocationSelect';

/** One document line for the warehouse screens: an item (chosen in the picker), the location it applies to, a quantity. */
export type WmsLine = {
  key: string;
  itemId: string;
  code: string;
  name: string;
  locationId: string;
  qty: number;
  item: PickItem;
  /** Screen-specific values (expected qty, condition, destination location, ...). */
  extra: Record<string, string | number>;
};

export type ExtraColumn = {
  key: string;
  label: string;
  width?: number;
  render: (line: WmsLine, patch: (extra: Record<string, string | number>) => void) => JSX.Element;
};

type Props = {
  lines: WmsLine[];
  onChange: (lines: WmsLine[]) => void;
  /** Pre-selects this location in the picker's filter (usually the document's warehouse). */
  warehouseId?: string;
  locationLabel?: string;
  qtyLabel?: string;
  /** Show "available at this location" under the quantity (issue, transfer, adjustment). */
  showAvailable?: boolean;
  /** Called when a location changes, so a screen can derive values from it (e.g. system quantity). */
  onLocationChange?: (line: WmsLine, locationId: string) => Partial<WmsLine> | void;
  /** Initial `extra` values for a freshly picked line. */
  defaultExtra?: (picked: PickedLine) => Record<string, string | number>;
  extraColumns?: ExtraColumn[];
  pickerTitle?: string;
  emptyText?: string;
};

const availableAt = (line: WmsLine): number => line.item.stock.find((s) => s.locationId === line.locationId)?.available ?? 0;

/**
 * The shared lines table of every warehouse document. Items come from the item-picker dialog (never typed ids); the
 * location is a dropdown; quantities and screen-specific fields are editable inline.
 */
export default function WmsLinesEditor({
  lines, onChange, warehouseId = '', locationLabel = 'الموقع', qtyLabel = 'الكمية', showAvailable = false,
  onLocationChange, defaultExtra, extraColumns = [], pickerTitle, emptyText = 'لا توجد أصناف بعد — اضغط «إضافة أصناف»',
}: Props): JSX.Element {
  const [open, setOpen] = useState(false);

  const add = useCallback((p: PickedLine): void => {
    if (!p.itemId) return;
    const itemId = p.itemId;
    const next = lines.slice();
    const existing = next.findIndex((l) => l.itemId === itemId && l.locationId === p.locationId);
    if (existing >= 0) {
      next[existing] = { ...next[existing], qty: Number(next[existing].qty) + p.quantity };
    } else {
      const created: WmsLine = {
        key: `${itemId}-${p.locationId}-${Date.now()}-${next.length}`,
        itemId, code: p.code, name: p.nameAr || p.name, locationId: p.locationId, qty: p.quantity, item: p.item,
        extra: defaultExtra ? defaultExtra(p) : {},
      };
      const derived = onLocationChange?.(created, p.locationId);
      next.push(derived ? { ...created, ...derived } : created);
    }
    onChange(next);
  }, [lines, onChange, defaultExtra, onLocationChange]);

  function patch(key: string, change: Partial<WmsLine>): void {
    onChange(lines.map((l) => (l.key === key ? { ...l, ...change } : l)));
  }

  return (
    <div>
      <div style={{ display: 'flex', gap: 10, marginBottom: 12, alignItems: 'center' }}>
        <button type="button" className="btn-primary" onClick={() => setOpen(true)}>🔍 إضافة أصناف</button>
        <span style={{ fontSize: 12, color: 'var(--txt-muted)' }}>{lines.length} سطر</span>
      </div>

      {lines.length === 0 ? (
        <div style={{ textAlign: 'center', padding: '28px 0', color: 'var(--txt-muted)', border: '1px dashed var(--clr-border)', borderRadius: 'var(--radius-md)' }}>{emptyText}</div>
      ) : (
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table" style={{ minWidth: 720 }}>
            <thead>
              <tr>
                <th>#</th><th>الصنف</th><th style={{ minWidth: 190 }}>{locationLabel}</th><th style={{ width: 110 }}>{qtyLabel}</th>
                {extraColumns.map((c) => <th key={c.key} style={c.width ? { width: c.width } : undefined}>{c.label}</th>)}
                <th />
              </tr>
            </thead>
            <tbody>
              {lines.map((l, i) => (
                <tr key={l.key}>
                  <td>{i + 1}</td>
                  <td>
                    <div style={{ fontFamily: 'monospace', fontWeight: 700, color: 'var(--clr-primary)' }}>{l.code}</div>
                    <div style={{ fontSize: 13 }}>{l.name}</div>
                  </td>
                  <td>
                    <LocationSelect
                      value={l.locationId}
                      allowEmpty={false}
                      onChange={(id) => {
                        const derived = onLocationChange?.(l, id);
                        patch(l.key, { locationId: id, ...(derived ?? {}) });
                      }}
                    />
                  </td>
                  <td>
                    <input type="number" min={0} className="vex-input" value={l.qty} onChange={(e) => patch(l.key, { qty: Number(e.target.value) })} />
                    {showAvailable ? <div style={{ fontSize: 11, color: Number(l.qty) > availableAt(l) ? 'var(--clr-warning)' : 'var(--txt-muted)' }}>متاح {availableAt(l).toLocaleString('en-US')}</div> : null}
                  </td>
                  {extraColumns.map((c) => (
                    <td key={c.key}>{c.render(l, (extra) => patch(l.key, { extra: { ...l.extra, ...extra } }))}</td>
                  ))}
                  <td><button type="button" className="btn-danger" style={{ padding: '4px 10px', fontSize: 12 }} onClick={() => onChange(lines.filter((x) => x.key !== l.key))}>✕</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <ItemPickerModal
        open={open}
        mode="warehouse"
        title={pickerTitle ?? 'اختيار الأصناف'}
        initialLocationId={warehouseId}
        onPick={add}
        onClose={() => setOpen(false)}
      />
    </div>
  );
}

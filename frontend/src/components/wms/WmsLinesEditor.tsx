import { useCallback, useState } from 'react';
import { Box, Button, IconButton, Paper, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography } from '@mui/material';
import type { PickItem } from '../../api/endpoints/lookups';
import { formatQty } from '../../lib/format';
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
    <Box>
      <Stack direction="row" gap={1.5} alignItems="center" sx={{ mb: 1.5 }}>
        <Button variant="contained" size="small" onClick={() => setOpen(true)}>🔍 إضافة أصناف</Button>
        <Typography variant="caption" color="text.secondary">{lines.length} سطر</Typography>
      </Stack>

      {lines.length === 0 ? (
        <Paper variant="outlined" sx={{ textAlign: 'center', py: 3.5, color: 'text.secondary', borderStyle: 'dashed', borderRadius: 2 }}>{emptyText}</Paper>
      ) : (
        <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 2 }}>
          <Table size="small" sx={{ minWidth: 720 }}>
            <TableHead>
              <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
                <TableCell>#</TableCell><TableCell>الصنف</TableCell><TableCell sx={{ minWidth: 190 }}>{locationLabel}</TableCell><TableCell sx={{ width: 120 }}>{qtyLabel}</TableCell>
                {extraColumns.map((c) => <TableCell key={c.key} sx={c.width ? { width: c.width } : undefined}>{c.label}</TableCell>)}
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {lines.map((l, i) => (
                <TableRow key={l.key}>
                  <TableCell>{i + 1}</TableCell>
                  <TableCell>
                    <Typography sx={{ fontFamily: 'monospace', fontWeight: 700 }} color="primary">{l.code}</Typography>
                    <Typography variant="body2">{l.name}</Typography>
                  </TableCell>
                  <TableCell>
                    <LocationSelect
                      value={l.locationId}
                      allowEmpty={false}
                      onChange={(id) => {
                        const derived = onLocationChange?.(l, id);
                        patch(l.key, { locationId: id, ...(derived ?? {}) });
                      }}
                    />
                  </TableCell>
                  <TableCell>
                    <TextField size="small" type="number" inputProps={{ min: 0 }} value={l.qty} onChange={(e) => patch(l.key, { qty: Number(e.target.value) })}
                      helperText={showAvailable ? `متاح ${formatQty(availableAt(l))}` : undefined}
                      FormHelperTextProps={{ sx: { color: Number(l.qty) > availableAt(l) ? 'warning.main' : undefined, mx: 0 } }} />
                  </TableCell>
                  {extraColumns.map((c) => (
                    <TableCell key={c.key}>{c.render(l, (extra) => patch(l.key, { extra: { ...l.extra, ...extra } }))}</TableCell>
                  ))}
                  <TableCell><IconButton size="small" color="error" aria-label="حذف السطر" onClick={() => onChange(lines.filter((x) => x.key !== l.key))}>✕</IconButton></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}

      <ItemPickerModal
        open={open}
        mode="warehouse"
        title={pickerTitle ?? 'اختيار الأصناف'}
        initialLocationId={warehouseId}
        onPick={add}
        onClose={() => setOpen(false)}
      />
    </Box>
  );
}

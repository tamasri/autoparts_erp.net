/**
 * Prepares a sales or purchase return from its original: each line of the original with what was sold / bought, what posted returns
 * already took back and what can still be returned. Prices, discounts and costs are the original's (the server fills them in, with
 * the return's share of the invoice discount); the return is saved as a draft and posted like any invoice.
 */
import { useEffect, useMemo, useState } from 'react';
import {
  Alert, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle, LinearProgress, Stack, Table, TableBody, TableCell, TableHead, TableRow,
  TextField, Typography,
} from '@mui/material';
import LocationSelect from '../../components/pickers/LocationSelect';
import Money from '../../components/ui/Money';
import { formatQty } from '../../lib/format';
import { extractApiError, toast } from '../../lib/toast';
import type { CreateReturn, ReturnableLine } from '../../api/endpoints/returns';

type Props = {
  open: boolean;
  title: string;
  /** Sales: returned goods may go to another location (a returns area) instead of where they left from. */
  chooseLocation?: boolean;
  load: () => Promise<ReturnableLine[]>;
  /** Saves the draft and gives back its id. */
  submit: (body: CreateReturn) => Promise<string>;
  onClose: () => void;
  onCreated: (id: string) => void;
};

const today = (): string => new Date().toLocaleDateString('en-CA');

export default function ReturnDialog({ open, title, chooseLocation = false, load, submit, onClose, onCreated }: Props): JSX.Element {
  const [lines, setLines] = useState<ReturnableLine[]>([]);
  const [qty, setQty] = useState<Record<string, string>>({});
  const [location, setLocation] = useState<Record<string, string>>({});
  const [date, setDate] = useState(today());
  const [reason, setReason] = useState('');
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!open) return;
    setQty({}); setLocation({}); setReason(''); setDate(today()); setError(''); setLoading(true);
    load().then(setLines).catch((e: unknown) => setError(extractApiError(e, 'تعذر تحميل أسطر الفاتورة'))).finally(() => setLoading(false));
  }, [open, load]);

  const chosen = useMemo(
    () => lines.map((l) => ({ line: l, q: Number(qty[l.lineId] || 0) })).filter((x) => x.q > 0),
    [lines, qty]);
  const invalid = chosen.find((x) => x.q > x.line.returnable);
  const value = chosen.reduce((t, x) => t + x.q * x.line.unitPriceUsd * (1 - x.line.discountPct / 100), 0);
  const nothingLeft = lines.length > 0 && lines.every((l) => l.returnable <= 0);

  async function save(): Promise<void> {
    setBusy(true);
    try {
      const id = await submit({
        returnDate: date, reason: reason.trim() || undefined,
        lines: chosen.map((x) => ({ lineId: x.line.lineId, quantity: x.q, locationId: location[x.line.lineId] || undefined })),
      });
      toast.success('حُفظ المرتجع مسودةً؛ رحّله لإرجاع البضاعة وتطبيق الرصيد');
      onCreated(id);
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر حفظ المرتجع'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent dividers>
        {loading ? <LinearProgress /> : null}
        {error ? <Alert severity="error">{error}</Alert> : null}
        {nothingLeft ? <Alert severity="info">أُرجعت كل كميات هذه الفاتورة.</Alert> : null}
        <Stack direction="row" gap={2} sx={{ mb: 2 }} flexWrap="wrap">
          <TextField size="small" type="date" label="تاريخ المرتجع" value={date} onChange={(e) => setDate(e.target.value)} InputLabelProps={{ shrink: true }} />
          <TextField size="small" label="السبب" value={reason} onChange={(e) => setReason(e.target.value)} sx={{ flex: 1, minWidth: 220 }} />
          <Button size="small" onClick={() => setQty(Object.fromEntries(lines.map((l) => [l.lineId, String(l.returnable)])))} disabled={nothingLeft}>إرجاع كل المتبقي</Button>
        </Stack>
        <Box sx={{ overflowX: 'auto' }}>
          <Table size="small">
            <TableHead>
              <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
                <TableCell>الصنف</TableCell><TableCell align="left">الكمية</TableCell><TableCell align="left">أُرجع</TableCell><TableCell align="left">المتبقي</TableCell>
                <TableCell align="left">السعر</TableCell><TableCell sx={{ minWidth: 110 }}>كمية المرتجع</TableCell>
                {chooseLocation ? <TableCell sx={{ minWidth: 200 }}>يعود إلى</TableCell> : null}
              </TableRow>
            </TableHead>
            <TableBody>
              {lines.map((l) => {
                const q = Number(qty[l.lineId] || 0);
                return (
                  <TableRow key={l.lineId}>
                    <TableCell><Typography component="span" sx={{ fontFamily: 'monospace', fontWeight: 700 }} color="primary">{l.code}</Typography> {l.name}</TableCell>
                    <TableCell align="left">{formatQty(l.quantity)}</TableCell>
                    <TableCell align="left">{formatQty(l.returned)}</TableCell>
                    <TableCell align="left">{formatQty(l.returnable)}</TableCell>
                    <TableCell align="left"><Money usd={l.unitPriceUsd} />{l.discountPct > 0 ? <Typography variant="caption" color="text.secondary"> −{l.discountPct}%</Typography> : null}</TableCell>
                    <TableCell>
                      <TextField size="small" type="number" value={qty[l.lineId] ?? ''} disabled={l.returnable <= 0}
                        inputProps={{ min: 0, max: l.returnable, step: 'any' }} error={q > l.returnable}
                        onChange={(e) => setQty({ ...qty, [l.lineId]: e.target.value })} />
                    </TableCell>
                    {chooseLocation ? (
                      <TableCell>
                        <LocationSelect value={location[l.lineId] ?? l.locationId ?? ''} allowEmpty={false}
                          onChange={(id) => setLocation({ ...location, [l.lineId]: id })} />
                      </TableCell>
                    ) : null}
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </Box>
        {invalid ? <Alert severity="error" sx={{ mt: 2 }}>الكمية المرتجعة من {invalid.line.code} أكبر من المتبقي ({formatQty(invalid.line.returnable)}).</Alert> : null}
        <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mt: 2 }}>
          <Typography variant="caption" color="text.secondary">بأسعار الفاتورة الأصلية؛ تُخصم حصة المرتجع من خصم الفاتورة عند الحفظ.</Typography>
          <Stack direction="row" gap={1} alignItems="center"><Typography variant="body2">قيمة الأسطر</Typography><Money usd={value} fontWeight={800} /></Stack>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>رجوع</Button>
        <Button variant="contained" disabled={busy || chosen.length === 0 || Boolean(invalid)} onClick={() => void save()}>حفظ المرتجع (مسودة)</Button>
      </DialogActions>
    </Dialog>
  );
}

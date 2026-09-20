/** Pay a supplier: the amount is spread over the supplier's open bills, oldest due date first (the user can see the split before saving). */
import { useEffect, useMemo, useState } from 'react';
import {
  Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography,
} from '@mui/material';
import { purchasingApi, type PurchaseInvoiceRow } from '../../api/endpoints/purchasing';
import { unwrapPaged } from '../../api/apiData';
import { extractApiError, toast } from '../../lib/toast';

const METHODS = [
  { value: 'CASH', label: 'نقداً (ل.س)' }, { value: 'USD_CASH', label: 'نقداً (دولار)' },
  { value: 'BANK_TRANSFER', label: 'حوالة مصرفية' }, { value: 'CHEQUE', label: 'شيك' },
];
const money = (v: number): string => v.toLocaleString('en-US', { maximumFractionDigits: 2 });

export function allocateOldestFirst(bills: PurchaseInvoiceRow[], amount: number): Array<{ purchaseInvoiceId: string; amountUsd: number }> {
  let left = amount;
  const out: Array<{ purchaseInvoiceId: string; amountUsd: number }> = [];
  for (const b of [...bills].sort((a, c) => a.dueDate.localeCompare(c.dueDate))) {
    if (left <= 0) break;
    const part = Math.min(left, b.balanceUsd);
    if (part > 0) { out.push({ purchaseInvoiceId: b.id, amountUsd: Math.round(part * 10000) / 10000 }); left -= part; }
  }
  return out;
}

type Props = { open: boolean; supplier: { id: string; name: string } | null; onClose: () => void; onSaved: () => void };

export default function SupplierPaymentDialog({ open, supplier, onClose, onSaved }: Props): JSX.Element {
  const [bills, setBills] = useState<PurchaseInvoiceRow[]>([]);
  const [method, setMethod] = useState('CASH');
  const [amount, setAmount] = useState(0);
  const [date, setDate] = useState(new Date().toLocaleDateString('en-CA'));
  const [reference, setReference] = useState('');
  const [chequeNumber, setChequeNumber] = useState('');
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!open || !supplier) return;
    setError(''); setAmount(0);
    purchasingApi.listInvoices({ page: 1, pageSize: 100, supplierPartyId: supplier.id, openOnly: true })
      .then((r) => setBills(unwrapPaged<PurchaseInvoiceRow>(r.data).items))
      .catch((e: unknown) => setError(extractApiError(e, 'تعذر تحميل فواتير المورّد المفتوحة')));
  }, [open, supplier]);

  const plan = useMemo(() => allocateOldestFirst(bills, amount), [bills, amount]);
  const owed = bills.reduce((a, b) => a + b.balanceUsd, 0);

  async function save(): Promise<void> {
    if (!supplier) return;
    if (!(amount > 0)) { setError('أدخل مبلغاً أكبر من صفر'); return; }
    if (amount > owed) { setError(`المبلغ أكبر من المستحق للمورّد ($${money(owed)})`); return; }
    if (method === 'CHEQUE' && !chequeNumber.trim()) { setError('رقم الشيك مطلوب'); return; }
    setSaving(true); setError('');
    try {
      await purchasingApi.createPayment({
        supplierPartyId: supplier.id, paymentDate: date, paymentMethod: method, amountUsd: amount,
        referenceNumber: reference.trim() || undefined, chequeNumber: chequeNumber.trim() || undefined, allocations: plan,
      });
      toast.success('تم تسجيل الدفعة للمورّد');
      onSaved(); onClose();
    } catch (e: unknown) {
      setError(extractApiError(e, 'تعذر تسجيل الدفعة'));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>دفعة للمورّد: {supplier?.name}</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2}>
          {error ? <Alert severity="error">{error}</Alert> : null}
          <Typography variant="body2" color="text.secondary">المستحق للمورّد: <strong>${money(owed)}</strong> على {bills.length} فاتورة</Typography>
          <Stack direction="row" gap={2} flexWrap="wrap">
            <TextField size="small" select label="طريقة الدفع" value={method} onChange={(e) => setMethod(e.target.value)} sx={{ minWidth: 170 }}>
              {METHODS.map((m) => <MenuItem key={m.value} value={m.value}>{m.label}</MenuItem>)}
            </TextField>
            <TextField size="small" type="number" label="المبلغ ($)" value={amount || ''} onChange={(e) => setAmount(Number(e.target.value))} />
            <TextField size="small" type="date" label="التاريخ" value={date} onChange={(e) => setDate(e.target.value)} InputLabelProps={{ shrink: true }} />
            {method === 'CHEQUE' ? <TextField size="small" label="رقم الشيك *" value={chequeNumber} onChange={(e) => setChequeNumber(e.target.value)} /> : null}
            {method === 'BANK_TRANSFER' ? <TextField size="small" label="رقم الحوالة" value={reference} onChange={(e) => setReference(e.target.value)} /> : null}
          </Stack>
          <Table size="small">
            <TableHead><TableRow sx={{ '& th': { fontWeight: 700 } }}><TableCell>الفاتورة</TableCell><TableCell>الاستحقاق</TableCell><TableCell align="left">المتبقي ($)</TableCell><TableCell align="left">سيُسدَّد ($)</TableCell></TableRow></TableHead>
            <TableBody>
              {bills.map((b) => (
                <TableRow key={b.id}>
                  <TableCell>{b.billNumber}</TableCell><TableCell>{b.dueDate}</TableCell><TableCell align="left">{money(b.balanceUsd)}</TableCell>
                  <TableCell align="left" sx={{ color: 'success.main', fontWeight: 700 }}>{money(plan.find((p) => p.purchaseInvoiceId === b.id)?.amountUsd ?? 0) || '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={onClose}>إلغاء</Button><Button variant="contained" disabled={saving} onClick={() => void save()}>تسجيل الدفعة</Button></DialogActions>
    </Dialog>
  );
}

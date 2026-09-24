import { useCallback, useState } from 'react';
import {
  Alert, Box, Button, Card, CardContent, Checkbox, Chip, FormControlLabel, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow,
  TextField, Typography,
} from '@mui/material';
import PageHeader from '../../components/ui/PageHeader';
import DataTable from '../../components/ui/DataTable';
import ReasonDialog from '../../components/ui/ReasonDialog';
import Money from '../../components/ui/Money';
import { formatSyp, formatUsd } from '../../lib/format';
import { paymentsApi, type AllocationLine } from '../../api/endpoints/payments';
import { customersApi } from '../../api/endpoints/customers';
import { invoicesApi } from '../../api/endpoints/invoices';
import { unwrapNode, unwrapPaged } from '../../api/apiData';
import { usePagedList } from '../../hooks/usePagedList';
import { toast, extractApiError } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';
import EntityPicker, { type PickerOption } from '../../components/pickers/EntityPicker';
import ExportMenu from '../../components/ui/ExportMenu';
import { num, ymd, type ExportDocument } from '../../lib/exportClient';
import DocumentViewButton from '../../components/ui/DocumentViewButton';
import { paymentDocument } from '../../lib/wmsDocuments';
import FxRateField from '../../components/pickers/FxRateField';

type Payment = {
  id: string; paymentNumber: string; paymentType: string; customerId: string; customerName: string; paymentDate: string;
  paymentMethod: string; amountSyp: number; amountUsd: number; unallocatedSyp: number; unallocatedUsd: number; isReversed: boolean;
};
type CustomerRow = { id: string; code?: string; name?: string; phone?: string };
type OpenInvoice = { id: string; invoiceNumber: string; balanceSyp: number; balanceUsd: number; dueDate: string };

const METHODS = [
  { value: 'CASH', label: 'نقداً (ل.س)' },
  { value: 'USD_CASH', label: 'نقداً (دولار)' },
  { value: 'BANK_TRANSFER', label: 'حوالة مصرفية' },
  { value: 'CHEQUE', label: 'شيك' },
];
const methodLabel = (m: string): string => METHODS.find((x) => x.value === m)?.label ?? m;
const today = (): string => new Date().toLocaleDateString('en-CA');

/** The printed receipt voucher (server-side form with the company's details). */
const loadReceiptPdf = async (id: string): Promise<Blob> => (await paymentsApi.getPdf(id)).data as Blob;

async function loadPaymentDocument(id: string): Promise<ExportDocument> {
  const p = unwrapNode<Payment>((await paymentsApi.get(id)).data);
  if (!p) throw new Error('السند غير موجود');
  return paymentDocument(p, methodLabel(p.paymentMethod));
}

/** Oldest-first allocation of a receipt over the customer's open invoices, per currency. */
function autoAllocate(invoices: OpenInvoice[], amountSyp: number, amountUsd: number): AllocationLine[] {
  let syp = amountSyp; let usd = amountUsd;
  const out: AllocationLine[] = [];
  for (const inv of [...invoices].sort((a, b) => a.dueDate.localeCompare(b.dueDate))) {
    if (syp <= 0 && usd <= 0) break;
    const s = Math.min(syp, Math.max(inv.balanceSyp, 0));
    const u = Math.min(usd, Math.max(inv.balanceUsd, 0));
    if (s > 0 || u > 0) { out.push({ invoiceId: inv.id, allocatedSyp: s, allocatedUsd: u }); syp -= s; usd -= u; }
  }
  return out;
}

export default function Payments(): JSX.Element {
  const [filterCustomer, setFilterCustomer] = useState<PickerOption | null>(null);
  const [filterMethod, setFilterMethod] = useState('');
  const list = usePagedList<Payment>({
    errorMessage: 'تعذر تحميل الدفعات',
    deps: [filterCustomer?.id, filterMethod],
    fetcher: ({ page, pageSize }) => paymentsApi.list({ page, pageSize, customerId: filterCustomer?.id, paymentMethod: filterMethod || undefined }),
  });

  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState('');
  const [formError, setFormError] = useState('');
  const [customer, setCustomer] = useState<PickerOption | null>(null);
  const [method, setMethod] = useState('CASH');
  const [paymentDate, setPaymentDate] = useState(today());
  const [amount, setAmount] = useState(0);
  const [fxRateId, setFxRateId] = useState('');
  const [sellRate, setSellRate] = useState(0);
  const [reference, setReference] = useState('');
  const [bankName, setBankName] = useState('');
  const [chequeNumber, setChequeNumber] = useState('');
  const [chequeDate, setChequeDate] = useState('');
  const [notes, setNotes] = useState('');
  const [openInvoices, setOpenInvoices] = useState<OpenInvoice[]>([]);
  const [autoApply, setAutoApply] = useState(true);
  const [reverseId, setReverseId] = useState('');

  const buildExport = async (): Promise<ExportDocument> => {
    const data = unwrapPaged<Payment>((await paymentsApi.list({ page: 1, pageSize: 200, customerId: filterCustomer?.id, paymentMethod: filterMethod || undefined })).data);
    return {
      title: 'الدفعات والقبض', subtitle: `${data.totalCount} سند${data.totalCount > 200 ? ' (أول 200)' : ''}`, fileName: 'payments', fields: [],
      tables: [{
        columns: ['رقم السند', 'الزبون', 'التاريخ', 'الطريقة', 'المبلغ (ل.س)', 'المبلغ ($)', 'غير الموزّع (ل.س)', 'غير الموزّع ($)', 'الحالة'],
        rows: data.items.map((p) => [p.paymentNumber, p.customerName, ymd(p.paymentDate), methodLabel(p.paymentMethod), num(p.amountSyp), num(p.amountUsd), num(p.unallocatedSyp), num(p.unallocatedUsd), p.isReversed ? 'معكوسة' : 'فعّالة']),
        numericColumns: [4, 5, 6, 7],
      }],
    };
  };

  const isUsd = method === 'USD_CASH';
  const amountSyp = isUsd ? 0 : amount;
  const amountUsd = isUsd ? amount : 0;

  const searchCustomers = useCallback(async (text: string): Promise<PickerOption[]> => {
    const res = await customersApi.getCustomers({ page: 1, pageSize: 10, searchTerm: text || undefined, isActive: true });
    return unwrapPaged<CustomerRow>(res.data).items.map((c) => ({ id: c.id, label: c.name ?? c.id.slice(0, 8), sublabel: [c.code, c.phone].filter(Boolean).join(' · ') }));
  }, []);

  async function pickCustomer(c: PickerOption | null): Promise<void> {
    setCustomer(c); setOpenInvoices([]);
    if (!c) return;
    try {
      const res = await invoicesApi.getInvoices({ page: 1, pageSize: 100, status: 'POSTED', customerId: c.id });
      setOpenInvoices(unwrapPaged<OpenInvoice>(res.data).items.filter((i) => i.balanceSyp > 0 || i.balanceUsd > 0));
    } catch { /* balances are informational; allocation is retried server-side */ }
  }

  const plan = autoApply ? autoAllocate(openInvoices, amountSyp, amountUsd) : [];
  const invName = (id: string): string => openInvoices.find((i) => i.id === id)?.invoiceNumber ?? id.slice(0, 8);

  async function create(): Promise<void> {
    if (!customer) { setFormError('اختر الزبون'); return; }
    if (!(amount > 0)) { setFormError('أدخل مبلغاً أكبر من صفر'); return; }
    if (!fxRateId) { setFormError('لا يوجد سعر صرف — أضفه من شاشة أسعار الصرف'); return; }
    if (method === 'CHEQUE' && !chequeNumber.trim()) { setFormError('رقم الشيك مطلوب'); return; }
    setBusy('create'); setFormError('');
    try {
      const res = await paymentsApi.create({
        paymentType: 'RECEIPT', customerId: customer.id, paymentDate, paymentMethod: method, amountSyp, amountUsd, fxRateId,
        referenceNumber: reference.trim() || undefined, bankName: bankName.trim() || undefined,
        chequeNumber: chequeNumber.trim() || undefined, chequeDate: chequeDate || undefined, notes: notes.trim() || undefined,
      });
      if (notifyResult(res, 'تم تسجيل الدفعة') === 'done' && plan.length > 0) {
        const created = unwrapNode<{ id?: string }>(res.data);
        if (created?.id) {
          try { await paymentsApi.allocate(created.id, plan); toast.success(`وُزّعت الدفعة على ${plan.length} فاتورة`); }
          catch (e: unknown) { toast.error(extractApiError(e, 'سُجّلت الدفعة لكن تعذر توزيعها على الفواتير')); }
        }
      }
      setCustomer(null); setAmount(0); setReference(''); setBankName(''); setChequeNumber(''); setChequeDate(''); setNotes(''); setOpenInvoices([]);
      setShowForm(false); list.reload();
    } catch (e: unknown) { setFormError(extractApiError(e, 'تعذر تسجيل الدفعة')); }
    finally { setBusy(''); }
  }

  async function reverse(reason: string): Promise<void> {
    setBusy(reverseId);
    try {
      const res = await paymentsApi.reverse(reverseId, reason);
      notifyResult(res, 'تم عكس الدفعة وإعادة الرصيد للفواتير');
      setReverseId(''); list.reload();
    } catch (e: unknown) { toast.error(extractApiError(e, 'تعذر عكس الدفعة')); }
    finally { setBusy(''); }
  }

  return (
    <Box>
      <PageHeader
        title="الدفعات والقبض" subtitle="سندات قبض الزبائن وتوزيعها على الفواتير المفتوحة"
        actions={<Stack direction="row" gap={1}><ExportMenu build={buildExport} /><Button variant={showForm ? 'outlined' : 'contained'} onClick={() => setShowForm((s) => !s)}>{showForm ? '✕ إلغاء' : '＋ سند قبض'}</Button></Stack>}
      />
      {list.error ? <Alert severity="error" sx={{ mb: 2 }}>{list.error}</Alert> : null}

      {showForm ? (
        <Card variant="outlined" sx={{ borderRadius: 3, mb: 3 }}>
          <CardContent>
            <Typography variant="h6" fontWeight={700} sx={{ mb: 2 }}>سند قبض جديد</Typography>
            {formError ? <Alert severity="error" sx={{ mb: 2 }}>{formError}</Alert> : null}
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: 2, mb: 2 }}>
              <EntityPicker label="الزبون *" value={customer} onChange={(c) => void pickCustomer(c)} search={searchCustomers} placeholder="ابحث بالاسم أو الكود أو الهاتف..." />
              <TextField select size="small" label="طريقة الدفع" value={method} onChange={(e) => setMethod(e.target.value)}>
                {METHODS.map((m) => <MenuItem key={m.value} value={m.value}>{m.label}</MenuItem>)}
              </TextField>
              <TextField size="small" type="number" label={`المبلغ (${isUsd ? '$' : 'ل.س'}) *`} inputProps={{ min: 0 }} value={amount || ''} onChange={(e) => setAmount(Number(e.target.value))}
                helperText={amount > 0 && sellRate > 0 ? `≈ ${isUsd ? formatSyp(amount * sellRate) : formatUsd(amount / sellRate)}` : undefined} />
              <TextField size="small" type="date" label="تاريخ الدفعة" InputLabelProps={{ shrink: true }} value={paymentDate} onChange={(e) => setPaymentDate(e.target.value)} />
              {method === 'BANK_TRANSFER' ? (
                <>
                  <TextField size="small" label="المصرف" value={bankName} onChange={(e) => setBankName(e.target.value)} />
                  <TextField size="small" label="رقم الحوالة" value={reference} onChange={(e) => setReference(e.target.value)} />
                </>
              ) : null}
              {method === 'CHEQUE' ? (
                <>
                  <TextField size="small" label="رقم الشيك *" value={chequeNumber} onChange={(e) => setChequeNumber(e.target.value)} />
                  <TextField size="small" type="date" label="تاريخ الشيك" InputLabelProps={{ shrink: true }} value={chequeDate} onChange={(e) => setChequeDate(e.target.value)} />
                  <TextField size="small" label="المصرف" value={bankName} onChange={(e) => setBankName(e.target.value)} />
                </>
              ) : null}
              <TextField size="small" label="ملاحظات" value={notes} onChange={(e) => setNotes(e.target.value)} />
            </Box>
            <Box sx={{ mb: 2 }}>
              <FxRateField value={fxRateId} onChange={(id, r) => { setFxRateId(id); setSellRate(r?.midRate ?? 0); }} documentDate={paymentDate} />
            </Box>

            {customer ? (
              <Box sx={{ mb: 2 }}>
                <FormControlLabel control={<Checkbox checked={autoApply} onChange={(e) => setAutoApply(e.target.checked)} />} label="توزيع تلقائي على الفواتير المفتوحة (الأقدم استحقاقاً أولاً)" />
                {openInvoices.length === 0 ? (
                  <Typography variant="body2" color="text.secondary">لا توجد فواتير مرحّلة بأرصدة مفتوحة لهذا الزبون — ستُسجَّل الدفعة كرصيد دائن غير موزّع.</Typography>
                ) : (
                  <Paper variant="outlined" sx={{ borderRadius: 2 }}>
                    <Table size="small">
                      <TableHead><TableRow sx={{ '& th': { fontWeight: 700 } }}><TableCell>الفاتورة</TableCell><TableCell>الاستحقاق</TableCell><TableCell align="left">الرصيد</TableCell><TableCell align="left">سيُسدَّد</TableCell></TableRow></TableHead>
                      <TableBody>
                        {openInvoices.map((i) => {
                          const p = plan.find((x) => x.invoiceId === i.id);
                          return (
                            <TableRow key={i.id}>
                              <TableCell sx={{ fontWeight: 600 }}>{i.invoiceNumber}</TableCell><TableCell>{i.dueDate}</TableCell>
                              <TableCell align="left"><Money usd={i.balanceUsd} syp={i.balanceSyp} /></TableCell>
                              <TableCell align="left">{p ? <Typography component="span" fontWeight={700} color="success.main">{[p.allocatedSyp > 0 ? formatSyp(p.allocatedSyp) : '', p.allocatedUsd > 0 ? formatUsd(p.allocatedUsd) : ''].filter(Boolean).join(' · ')}</Typography> : '—'}</TableCell>
                            </TableRow>
                          );
                        })}
                      </TableBody>
                    </Table>
                  </Paper>
                )}
                {plan.length > 0 ? <Typography variant="caption" color="text.secondary">{plan.map((p) => invName(p.invoiceId)).join('، ')}</Typography> : null}
              </Box>
            ) : null}

            <Button variant="contained" disabled={busy === 'create'} onClick={() => void create()}>💾 تسجيل الدفعة</Button>
          </CardContent>
        </Card>
      ) : null}

      <Stack direction="row" gap={2} flexWrap="wrap" sx={{ mb: 2 }}>
        <Box sx={{ minWidth: 260 }}><EntityPicker label="الزبون" value={filterCustomer} onChange={setFilterCustomer} search={searchCustomers} placeholder="كل الزبائن" /></Box>
        <TextField select size="small" label="الطريقة" value={filterMethod} onChange={(e) => setFilterMethod(e.target.value)} sx={{ minWidth: 180 }}>
          <MenuItem value="">الكل</MenuItem>{METHODS.map((m) => <MenuItem key={m.value} value={m.value}>{m.label}</MenuItem>)}
        </TextField>
      </Stack>

      <DataTable
        rows={list.items} getKey={(p) => p.id} loading={list.loading} empty="لا توجد دفعات"
        paging={{ page: list.page - 1, pageSize: list.pageSize, total: list.totalCount, onPage: (p) => list.setPage(p + 1), onPageSize: list.changePageSize }}
        columns={[
          { header: 'رقم السند', render: (p) => <Typography fontWeight={700} color="primary" sx={{ opacity: p.isReversed ? 0.55 : 1 }}>{p.paymentNumber}</Typography>, nowrap: true },
          { header: 'الزبون', render: (p) => p.customerName },
          { header: 'التاريخ', render: (p) => p.paymentDate, nowrap: true },
          { header: 'الطريقة', render: (p) => methodLabel(p.paymentMethod) },
          { header: 'المبلغ', render: (p) => <Money usd={p.amountUsd} syp={p.amountSyp} fontWeight={700} />, numeric: true },
          { header: 'غير الموزّع', render: (p) => (p.unallocatedUsd > 0 || p.unallocatedSyp > 0 ? <Money usd={p.unallocatedUsd} syp={p.unallocatedSyp} /> : '—'), numeric: true },
          { header: 'الحالة', render: (p) => <Chip size="small" variant="outlined" color={p.isReversed ? 'error' : 'success'} label={p.isReversed ? 'معكوسة' : 'فعّالة'} /> },
          {
            header: ' ', nowrap: true,
            render: (p) => (
              <Stack direction="row" gap={1}>
                <DocumentViewButton browse={{ kind: 'payments', id: p.id, load: loadPaymentDocument, pdf: loadReceiptPdf }} />
                {!p.isReversed ? <Button size="small" color="error" onClick={() => setReverseId(p.id)}>↩ عكس</Button> : null}
              </Stack>
            ),
          },
        ]}
      />

      <ReasonDialog open={Boolean(reverseId)} title="عكس الدفعة — ستُعاد المبالغ الموزّعة إلى أرصدة الفواتير ويُلغى القيد في المحاسبة" confirmLabel="تأكيد العكس" minLength={5}
        onClose={() => setReverseId('')} onConfirm={reverse} />
    </Box>
  );
}

import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert, Box, Button, Card, CardContent, Divider, IconButton, MenuItem, Paper, Stack, Step, StepLabel, Stepper, Table, TableBody, TableCell,
  TableContainer, TableHead, TableRow, TextField, Typography,
} from '@mui/material';
import PageHeader from '../../components/ui/PageHeader';
import Money from '../../components/ui/Money';
import { formatQty, formatUsd } from '../../lib/format';
import { useNavigate } from 'react-router-dom';
import { invoicesApi, type CreateInvoice } from '../../api/endpoints/invoices';
import { customersApi } from '../../api/endpoints/customers';
import { lookupsApi, type PickItem } from '../../api/endpoints/lookups';
import { salesRepsApi, type SalesRep } from '../../api/endpoints/salesReps';
import { unwrapNode, unwrapPaged } from '../../api/apiData';
import EntityPicker, { type PickerOption } from '../../components/pickers/EntityPicker';
import FxRateField, { type FxRate } from '../../components/pickers/FxRateField';
import ItemPickerModal, { type PickedLine } from '../../components/pickers/ItemPickerModal';

type CustomerRecord = {
  id: string;
  code?: string;
  name?: string;
  phone?: string;
  paymentTermsDays?: number;
  creditLimitSyp?: number;
  creditLimitUsd?: number;
  assignedSalesRep?: string | null;
};

/** One invoice line as edited on screen: the payload fields plus what the picker already knows about the item. */
type Line = {
  key: string;
  skuId: string;
  code: string;
  name: string;
  locationId: string;
  batchId: string;
  quantity: number;
  unitPriceSyp: number;
  unitPriceUsd: number;
  discountPct: number;
  overrideReason: string;
  minPriceSyp: number;
  minPriceUsd: number;
  item: PickItem;
};

// Local calendar date (toISOString() is UTC and shifts the day for users ahead of/behind UTC).
const toLocalDate = (d: Date): string => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const today = toLocalDate(new Date());

function extractError(e: unknown, fallback: string): string {
  const r = e as { response?: { data?: { detail?: string; message?: string } } };
  return r.response?.data?.detail ?? r.response?.data?.message ?? fallback;
}

const addDays = (date: string, days: number): string => {
  const d = new Date(`${date}T00:00:00`);
  d.setDate(d.getDate() + days);
  return toLocalDate(d);
};

const STEPS = [
  { label: 'بيانات الفاتورة', sublabel: 'الزبون والتاريخ وسعر الصرف' },
  { label: 'الأسطر', sublabel: 'الأصناف والكميات' },
  { label: 'المراجعة', sublabel: 'التحقق والحفظ' },
];

const availableFor = (l: Line): number => {
  if (l.item.isBatchTracked && l.batchId) return l.item.batches.find((b) => b.id === l.batchId)?.quantity ?? 0;
  return l.item.stock.find((s) => s.locationId === l.locationId)?.available ?? 0;
};

const belowMinimum = (l: Line): boolean =>
  (l.minPriceSyp > 0 && l.unitPriceSyp < l.minPriceSyp) || (l.minPriceUsd > 0 && l.unitPriceUsd < l.minPriceUsd);

const lineTotals = (l: Line): { syp: number; usd: number } => {
  const factor = Number(l.quantity) * (1 - Number(l.discountPct) / 100);
  return { syp: factor * Number(l.unitPriceSyp), usd: factor * Number(l.unitPriceUsd) };
};

export default function InvoiceWorkspace(): JSX.Element {
  const navigate = useNavigate();
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [currentStep, setCurrentStep] = useState(0);

  const [customer, setCustomer] = useState<PickerOption | null>(null);
  const [invoiceDate, setInvoiceDate] = useState(today);
  const [dueDate, setDueDate] = useState(today);
  const [dueTouched, setDueTouched] = useState(false);
  const [fxRateId, setFxRateId] = useState('');
  const [fxRate, setFxRate] = useState<FxRate | null>(null);
  const [invoiceType, setInvoiceType] = useState('SALE');
  const [deliveryFeeSyp, setDeliveryFeeSyp] = useState(0);
  const [deliveryFeeUsd, setDeliveryFeeUsd] = useState(0);
  const [lines, setLines] = useState<Line[]>([]);
  // Discount on the whole invoice, on top of each line's own discount.
  const [discountMode, setDiscountMode] = useState<'pct' | 'usd'>('pct');
  const [discountValue, setDiscountValue] = useState(0);
  // Reps the user may see (all, or only themselves); empty when the user is neither a rep nor allowed to see reps.
  const [reps, setReps] = useState<SalesRep[]>([]);
  const [salesRepId, setSalesRepId] = useState('');
  useEffect(() => {
    salesRepsApi.list({}).then((r) => setReps((unwrapNode<SalesRep[]>(r.data) ?? []).filter((x) => x.isActive))).catch(() => setReps([]));
  }, []);

  const [pickerOpen, setPickerOpen] = useState(false);
  const [pickerSearch, setPickerSearch] = useState('');
  const [scan, setScan] = useState('');
  const [scanNote, setScanNote] = useState('');

  const isReturn = invoiceType === 'RETURN';
  const customerRecord = customer?.data as CustomerRecord | undefined;

  const searchCustomers = useCallback(async (text: string): Promise<PickerOption[]> => {
    const res = await customersApi.getCustomers({ page: 1, pageSize: 10, searchTerm: text || undefined, isActive: true });
    return unwrapPaged<CustomerRecord>(res.data).items.map((c) => ({
      id: c.id,
      label: c.name ?? c.id.slice(0, 8),
      sublabel: [c.code, c.phone, c.paymentTermsDays ? `استحقاق ${c.paymentTermsDays} يوم` : ''].filter(Boolean).join(' · '),
      data: c,
    }));
  }, []);

  // The rep follows the customer's own rep whenever the customer changes.
  useEffect(() => { setSalesRepId(customerRecord?.assignedSalesRep ?? ''); }, [customerRecord]);

  // The due date follows the customer's payment terms until the user edits it by hand.
  useEffect(() => {
    if (dueTouched) return;
    const terms = customerRecord?.paymentTermsDays ?? 0;
    setDueDate(addDays(invoiceDate, terms > 0 ? terms : 0));
  }, [invoiceDate, customerRecord, dueTouched]);

  // F2 opens the item picker while editing lines.
  useEffect(() => {
    if (currentStep !== 1) return undefined;
    const onKey = (e: KeyboardEvent): void => {
      if (e.key === 'F2') { e.preventDefault(); setPickerSearch(''); setPickerOpen(true); }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [currentStep]);

  const addFromPick = useCallback((p: PickedLine): void => {
    if (!p.skuId) return;
    setError('');
    setLines((prev) => {
      const existing = prev.findIndex((l) => l.skuId === p.skuId && l.locationId === p.locationId && l.batchId === (p.batchId ?? ''));
      if (existing >= 0) {
        return prev.map((l, i) => (i === existing ? { ...l, quantity: Number(l.quantity) + p.quantity } : l));
      }
      return [...prev, {
        key: `${p.skuId}-${Date.now()}-${prev.length}`,
        skuId: p.skuId as string,
        code: p.code,
        name: p.nameAr || p.name,
        locationId: p.locationId,
        batchId: p.batchId ?? '',
        quantity: p.quantity,
        unitPriceSyp: p.unitPriceSyp,
        unitPriceUsd: p.unitPriceUsd,
        discountPct: 0,
        overrideReason: '',
        minPriceSyp: p.minPriceSyp,
        minPriceUsd: p.minPriceUsd,
        item: p.item,
      }];
    });
  }, []);

  function patchLine(key: string, patch: Partial<Line>): void {
    setLines((prev) => prev.map((l) => {
      if (l.key !== key) return l;
      const next = { ...l, ...patch };
      if (patch.locationId !== undefined && patch.locationId !== l.locationId) {
        next.batchId = l.item.isBatchTracked ? (l.item.batches.find((b) => b.locationId === patch.locationId)?.id ?? '') : '';
      }
      return next;
    }));
  }

  async function handleScan(): Promise<void> {
    const code = scan.trim();
    if (!code) return;
    setScanNote('');
    try {
      const res = await lookupsApi.pickItems({ search: code, mode: 'sales', inStockOnly: !isReturn, page: 1, pageSize: 2 });
      const found = unwrapPaged<PickItem>(res.data);
      if (found.items.length === 1) {
        const it = found.items[0];
        const best = [...it.stock].filter((s) => isReturn || s.available > 0).sort((a, b) => b.available - a.available)[0];
        const batch = it.isBatchTracked ? it.batches.find((b) => b.locationId === best?.locationId) : undefined;
        if (!it.skuId || it.isStopShip || !best || (it.isBatchTracked && !batch && !isReturn)) {
          setScanNote(it.isStopShip ? `الصنف ${it.code} موقوف الشحن` : `تعذّر إضافة ${it.code} تلقائياً — اختره من النافذة`);
          setPickerSearch(code); setPickerOpen(true);
        } else {
          addFromPick({
            itemId: it.itemId, skuId: it.skuId, code: it.code, name: it.name, nameAr: it.nameAr,
            locationId: best.locationId, locationCode: best.locationCode, batchId: batch?.id, batchNumber: batch?.batchNumber,
            quantity: 1, unitPriceSyp: it.sellingPriceSyp, unitPriceUsd: it.sellingPriceUsd,
            minPriceSyp: it.minSellingPriceSyp, minPriceUsd: it.minSellingPriceUsd, available: best.available,
            isBatchTracked: it.isBatchTracked, item: it,
          });
          setScanNote(`✓ أُضيف ${it.code}`);
        }
      } else {
        setPickerSearch(code);
        setPickerOpen(true);
      }
      setScan('');
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر البحث عن الصنف'));
    }
  }

  const subtotal = useMemo(() => lines.reduce((acc, l) => {
    const t = lineTotals(l);
    return { syp: acc.syp + t.syp, usd: acc.usd + t.usd };
  }, { syp: 0, usd: 0 }), [lines]);

  // Same rule as the server: a percentage follows the lines; a dollar amount is converted at the invoice's rate.
  const discount = useMemo(() => {
    const v = Math.max(Number(discountValue) || 0, 0);
    if (discountMode === 'pct') return { syp: subtotal.syp * Math.min(v, 100) / 100, usd: subtotal.usd * Math.min(v, 100) / 100 };
    return { syp: v * Number(fxRate?.midRate ?? 0), usd: v };
  }, [discountMode, discountValue, subtotal, fxRate]);

  const totals = useMemo(() => ({
    syp: subtotal.syp - discount.syp + Number(deliveryFeeSyp),
    usd: subtotal.usd - discount.usd + Number(deliveryFeeUsd),
  }), [subtotal, discount, deliveryFeeSyp, deliveryFeeUsd]);

  // The credit limit is kept in dollars; what the customer already owes comes from their statement.
  const [owedUsd, setOwedUsd] = useState(0);
  useEffect(() => {
    if (!customerRecord?.id) { setOwedUsd(0); return; }
    let live = true;
    customersApi.getCustomerStatement(customerRecord.id)
      .then((r) => { if (live) setOwedUsd(Number(unwrapNode<{ outstandingUsd?: number }>(r.data)?.outstandingUsd ?? 0)); })
      .catch(() => { if (live) setOwedUsd(0); });
    return () => { live = false; };
  }, [customerRecord?.id]);

  const creditWarning = useMemo(() => {
    const limit = Number(customerRecord?.creditLimitUsd ?? 0);
    if (!customerRecord || limit <= 0 || isReturn) return '';
    const projected = owedUsd + totals.usd;
    return projected > limit ? `تجاوز الحد الائتماني: الرصيد بعد الفاتورة ${formatUsd(projected)} من أصل ${formatUsd(limit)}` : '';
  }, [customerRecord, owedUsd, totals.usd, isReturn]);

  function lineProblems(): string {
    if (lines.length === 0) return 'أضف صنفاً واحداً على الأقل';
    for (const l of lines) {
      if (!(Number(l.quantity) > 0)) return `الكمية غير صحيحة للصنف ${l.code}`;
      if (!l.locationId) return `اختر الموقع للصنف ${l.code}`;
      if (!isReturn && Number(l.quantity) > availableFor(l)) return `الكمية تتجاوز المتاح للصنف ${l.code} (${formatQty(availableFor(l))})`;
      if (!isReturn && l.item.isBatchTracked && !l.batchId) return `اختر الدفعة للصنف ${l.code}`;
      if (belowMinimum(l) && !l.overrideReason.trim()) return `السعر أقل من الحد الأدنى للصنف ${l.code} — اكتب سبب التجاوز`;
    }
    if (discountMode === 'pct' && Number(discountValue) > 100) return 'نسبة خصم الفاتورة لا تتجاوز 100%';
    if (discountMode === 'usd' && Number(discountValue) > subtotal.usd) return 'خصم الفاتورة أكبر من مجموع البنود';
    return '';
  }

  function goNext(): void {
    if (currentStep === 0) {
      if (!customer) { setError('اختر الزبون'); return; }
      if (!fxRateId) { setError('لا يوجد سعر صرف — أضف سعراً من الإعدادات'); return; }
    }
    if (currentStep === 1) {
      const problem = lineProblems();
      if (problem) { setError(problem); return; }
    }
    setError('');
    setCurrentStep((s) => Math.min(s + 1, STEPS.length - 1));
  }

  async function submit(): Promise<void> {
    const problem = !customer ? 'اختر الزبون' : !fxRateId ? 'لا يوجد سعر صرف' : lineProblems();
    if (problem) { setError(problem); return; }
    setBusy(true);
    setError('');
    try {
      const payload: CreateInvoice = {
        customerId: (customer as PickerOption).id,
        invoiceDate,
        dueDate,
        fxRateId,
        invoiceType,
        salesRepId: salesRepId || undefined,
        deliveryFeeSyp: Number(deliveryFeeSyp),
        deliveryFeeUsd: Number(deliveryFeeUsd),
        discountPct: discountMode === 'pct' && Number(discountValue) > 0 ? Number(discountValue) : undefined,
        discountAmountUsd: discountMode === 'usd' && Number(discountValue) > 0 ? Number(discountValue) : undefined,
        lines: lines.map((l) => ({
          skuId: l.skuId,
          batchId: l.batchId || undefined,
          locationId: l.locationId,
          quantity: Number(l.quantity),
          unitPriceSyp: Number(l.unitPriceSyp),
          unitPriceUsd: Number(l.unitPriceUsd),
          discountPct: Number(l.discountPct),
          isPriceOverride: belowMinimum(l),
          overrideReason: belowMinimum(l) ? l.overrideReason.trim() : undefined,
        })),
      };
      const res = await invoicesApi.createInvoice(payload);
      const created = unwrapNode<{ id?: string }>(res.data);
      navigate(created?.id ? `/invoices/${created.id}` : '/invoices');
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر إنشاء الفاتورة'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Box>
      <PageHeader
        title={isReturn ? 'مرتجع جديد' : 'فاتورة جديدة'}
        crumbs={[{ label: 'الفواتير', to: '/invoices' }, { label: 'إنشاء' }]}
        actions={<Button onClick={() => navigate('/invoices')}>← رجوع</Button>}
      />
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}

      <Card variant="outlined" sx={{ borderRadius: 3 }}>
        <CardContent>
          <Stepper activeStep={currentStep} alternativeLabel sx={{ mb: 3 }}>
            {STEPS.map((s) => <Step key={s.label}><StepLabel optional={<Typography variant="caption" color="text.secondary">{s.sublabel}</Typography>}>{s.label}</StepLabel></Step>)}
          </Stepper>

          {currentStep === 0 ? (
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))', gap: 2 }}>
              <Box sx={{ gridColumn: 'span 2' }}>
                <EntityPicker id="invoice-customer" label="الزبون *" value={customer} onChange={setCustomer} search={searchCustomers} placeholder="ابحث بالاسم أو الكود أو الهاتف..." />
              </Box>
              <TextField id="invoice-type" select size="small" label="نوع الفاتورة" value={invoiceType} onChange={(e) => { setInvoiceType(e.target.value); setLines([]); }}>
                <MenuItem value="SALE">بيع</MenuItem><MenuItem value="RETURN">مرتجع</MenuItem>
              </TextField>
              <TextField id="invoice-date" size="small" type="date" label="تاريخ الفاتورة" InputLabelProps={{ shrink: true }} value={invoiceDate} onChange={(e) => setInvoiceDate(e.target.value)} />
              <TextField id="invoice-due-date" size="small" type="date" label="تاريخ الاستحقاق" InputLabelProps={{ shrink: true }} value={dueDate}
                onChange={(e) => { setDueDate(e.target.value); setDueTouched(true); }}
                helperText={customerRecord?.paymentTermsDays ? `شروط الزبون: ${customerRecord.paymentTermsDays} يوم` : undefined} />
              {reps.length > 0 ? (
                <TextField id="invoice-sales-rep" select size="small" label="المندوب" value={salesRepId} onChange={(e) => setSalesRepId(e.target.value)} SelectProps={{ displayEmpty: true }} InputLabelProps={{ shrink: true }}>
                  <MenuItem value="">— بدون مندوب —</MenuItem>
                  {reps.map((r) => <MenuItem key={r.userId} value={r.userId}>{r.fullName}</MenuItem>)}
                </TextField>
              ) : null}
              <Box sx={{ gridColumn: 'span 2' }}>
                <Typography variant="caption" color="text.secondary">سعر الصرف</Typography>
                <FxRateField value={fxRateId} onChange={(id, rate) => { setFxRateId(id); setFxRate(rate); }} documentDate={invoiceDate} />
              </Box>
              <TextField id="invoice-delivery-syp" size="small" type="number" label="أجور التوصيل (ل.س)" inputProps={{ min: 0 }} value={deliveryFeeSyp} onChange={(e) => setDeliveryFeeSyp(Number(e.target.value))} />
              <TextField id="invoice-delivery-usd" size="small" type="number" label="أجور التوصيل ($)" inputProps={{ min: 0 }} value={deliveryFeeUsd} onChange={(e) => setDeliveryFeeUsd(Number(e.target.value))} />
            </Box>
          ) : null}

          {currentStep === 1 ? (
            <Stack spacing={2}>
              <Stack direction="row" gap={1.5} flexWrap="wrap" alignItems="center">
                <Button variant="contained" onClick={() => { setPickerSearch(''); setPickerOpen(true); }}>🔍 اختيار أصناف (F2)</Button>
                <TextField size="small" sx={{ flex: '1 1 260px', maxWidth: 420 }} value={scan} placeholder="امسح الباركود أو اكتب الكود ثم Enter"
                  onChange={(e) => setScan(e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); void handleScan(); } }} />
                {scanNote ? <Typography variant="body2" color={scanNote.startsWith('✓') ? 'success.main' : 'warning.main'}>{scanNote}</Typography> : null}
              </Stack>

              {lines.length === 0 ? (
                <Paper variant="outlined" sx={{ textAlign: 'center', py: 5, color: 'text.secondary', borderStyle: 'dashed', borderRadius: 2 }}>لا توجد أصناف بعد — اضغط «اختيار أصناف» أو امسح الباركود</Paper>
              ) : (
                <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 2 }}>
                  <Table size="small" sx={{ minWidth: 1040 }}>
                    <TableHead>
                      <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
                        <TableCell>#</TableCell><TableCell>الصنف</TableCell><TableCell sx={{ minWidth: 150 }}>الموقع</TableCell>{!isReturn ? <TableCell sx={{ minWidth: 140 }}>الدفعة</TableCell> : null}
                        <TableCell sx={{ width: 110 }}>الكمية</TableCell><TableCell sx={{ width: 130 }}>السعر ل.س</TableCell><TableCell sx={{ width: 110 }}>السعر $</TableCell>
                        <TableCell sx={{ width: 90 }}>خصم %</TableCell><TableCell align="left">الإجمالي</TableCell><TableCell />
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {lines.map((l, idx) => {
                        const avail = availableFor(l);
                        const over = !isReturn && Number(l.quantity) > avail;
                        const below = belowMinimum(l);
                        const t = lineTotals(l);
                        const stockOpts = l.item.stock.filter((s) => isReturn || s.available > 0 || s.locationId === l.locationId);
                        const batches = l.item.batches.filter((b) => b.locationId === l.locationId);
                        return (
                          <TableRow key={l.key}>
                            <TableCell>{idx + 1}</TableCell>
                            <TableCell>
                              <Typography sx={{ fontFamily: 'monospace', fontWeight: 700 }} color="primary">{l.code}</Typography>
                              <Typography variant="body2">{l.name}</Typography>
                              {below ? <TextField size="small" fullWidth color="warning" focused sx={{ mt: 0.5 }} placeholder="سعر أقل من الحد الأدنى — سبب التجاوز *" value={l.overrideReason} onChange={(e) => patchLine(l.key, { overrideReason: e.target.value })} /> : null}
                            </TableCell>
                            <TableCell>
                              <TextField select size="small" fullWidth value={l.locationId} onChange={(e) => patchLine(l.key, { locationId: e.target.value })}>
                                {stockOpts.map((s) => <MenuItem key={s.locationId} value={s.locationId}>{s.locationCode} ({formatQty(s.available)})</MenuItem>)}
                              </TextField>
                            </TableCell>
                            {!isReturn ? (
                              <TableCell>
                                {l.item.isBatchTracked ? (
                                  <TextField select size="small" fullWidth value={l.batchId} onChange={(e) => patchLine(l.key, { batchId: e.target.value })} SelectProps={{ displayEmpty: true }}>
                                    <MenuItem value=""><em>— اختر —</em></MenuItem>
                                    {batches.map((b) => <MenuItem key={b.id} value={b.id}>{b.batchNumber} ({formatQty(b.quantity)})</MenuItem>)}
                                  </TextField>
                                ) : '—'}
                              </TableCell>
                            ) : null}
                            <TableCell>
                              <TextField size="small" type="number" inputProps={{ min: 0 }} error={over} value={l.quantity} onChange={(e) => patchLine(l.key, { quantity: Number(e.target.value) })}
                                helperText={!isReturn ? `متاح ${formatQty(avail)}` : undefined} FormHelperTextProps={{ sx: { mx: 0 } }} />
                            </TableCell>
                            <TableCell><TextField size="small" type="number" inputProps={{ min: 0 }} value={l.unitPriceSyp} onChange={(e) => patchLine(l.key, { unitPriceSyp: Number(e.target.value) })} /></TableCell>
                            <TableCell><TextField size="small" type="number" inputProps={{ min: 0 }} value={l.unitPriceUsd} onChange={(e) => patchLine(l.key, { unitPriceUsd: Number(e.target.value) })} /></TableCell>
                            <TableCell><TextField size="small" type="number" inputProps={{ min: 0, max: 100 }} value={l.discountPct} onChange={(e) => patchLine(l.key, { discountPct: Number(e.target.value) })} /></TableCell>
                            <TableCell align="left"><Money usd={t.usd} syp={t.syp} fontWeight={700} /></TableCell>
                            <TableCell><IconButton size="small" color="error" aria-label="حذف السطر" onClick={() => setLines((prev) => prev.filter((x) => x.key !== l.key))}>✕</IconButton></TableCell>
                          </TableRow>
                        );
                      })}
                    </TableBody>
                  </Table>
                </TableContainer>
              )}

              <Stack direction="row" gap={1.5} alignItems="flex-start" flexWrap="wrap">
                <TextField id="invoice-discount-mode" select size="small" label="خصم على الفاتورة" value={discountMode} onChange={(e) => setDiscountMode(e.target.value as 'pct' | 'usd')} sx={{ width: 160 }}>
                  <MenuItem value="pct">نسبة %</MenuItem><MenuItem value="usd">مبلغ $</MenuItem>
                </TextField>
                <TextField id="invoice-discount-value" size="small" type="number" label={discountMode === 'pct' ? 'النسبة %' : 'المبلغ $'} inputProps={{ min: 0, max: discountMode === 'pct' ? 100 : undefined }}
                  value={discountValue} onChange={(e) => setDiscountValue(Number(e.target.value))} sx={{ width: 140 }} />
                <Typography variant="body2" color="text.secondary" component="div" sx={{ pt: 1 }}>
                  مجموع البنود <Money usd={subtotal.usd} syp={subtotal.syp} inline variant="body2" />
                  {discount.usd > 0 ? <> — الخصم <Money usd={discount.usd} syp={discount.syp} inline variant="body2" /></> : null}
                </Typography>
              </Stack>

              <Paper sx={{ p: 2, borderRadius: 2, bgcolor: 'primary.main', color: 'primary.contrastText', display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 1 }}>
                <Typography fontWeight={600}>الإجمالي التقديري (شامل أجور التوصيل)</Typography>
                <Money usd={totals.usd} syp={totals.syp} variant="h5" fontWeight={800} color="inherit" />
              </Paper>
              {creditWarning ? <Alert severity="warning">{creditWarning}</Alert> : null}
            </Stack>
          ) : null}

          {currentStep === 2 ? (
            <Stack spacing={2}>
              <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: 2 }}>
                <Paper variant="outlined" sx={{ p: 2, borderRadius: 2 }}>
                  <Typography variant="caption" color="text.secondary" fontWeight={700}>بيانات الفاتورة</Typography>
                  <Stack spacing={1} sx={{ mt: 1 }}>
                    <ReviewRow label="الزبون" value={customer?.label ?? '—'} />
                    <ReviewRow label="النوع" value={isReturn ? 'مرتجع' : 'بيع'} />
                    <ReviewRow label="المندوب" value={reps.find((r) => r.userId === salesRepId)?.fullName ?? '—'} />
                    <ReviewRow label="التاريخ" value={invoiceDate} />
                    <ReviewRow label="الاستحقاق" value={dueDate} />
                    <ReviewRow label="سعر الصرف" value={fxRate ? `${formatQty(fxRate.midRate)} ${fxRate.currencyTo} (${fxRate.rateDate})` : '—'} />
                  </Stack>
                </Paper>
                <Paper variant="outlined" sx={{ p: 2, borderRadius: 2 }}>
                  <Typography variant="caption" color="text.secondary" fontWeight={700}>الملخص المالي</Typography>
                  <Stack spacing={1} sx={{ mt: 1 }}>
                    <ReviewRow label="عدد الأسطر" value={String(lines.length)} />
                    <ReviewRow label="مجموع البنود" value={<Money usd={subtotal.usd} syp={subtotal.syp} inline variant="body2" />} />
                    {discount.usd > 0 ? <ReviewRow label={discountMode === 'pct' ? `خصم الفاتورة ${discountValue}%` : 'خصم الفاتورة'} value={<Money usd={-discount.usd} syp={-discount.syp} inline variant="body2" />} /> : null}
                    <ReviewRow label="أجور التوصيل" value={<Money usd={deliveryFeeUsd} syp={deliveryFeeSyp} inline variant="body2" />} />
                    <Divider />
                    <ReviewRow label="الإجمالي" value={<Money usd={totals.usd} syp={totals.syp} inline variant="body1" fontWeight={800} color="primary.main" />} />
                  </Stack>
                </Paper>
              </Box>
              <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 2 }}>
                <Table size="small">
                  <TableHead><TableRow sx={{ '& th': { fontWeight: 700 } }}><TableCell>الصنف</TableCell><TableCell>الموقع</TableCell><TableCell align="left">الكمية</TableCell><TableCell align="left">السعر</TableCell><TableCell align="left">الإجمالي</TableCell></TableRow></TableHead>
                  <TableBody>
                    {lines.map((l) => (
                      <TableRow key={l.key}>
                        <TableCell><Typography component="span" sx={{ fontFamily: 'monospace', fontWeight: 700 }}>{l.code}</Typography> {l.name}</TableCell>
                        <TableCell>{l.item.stock.find((s) => s.locationId === l.locationId)?.locationCode ?? '—'}</TableCell>
                        <TableCell align="left">{formatQty(l.quantity)}</TableCell>
                        <TableCell align="left"><Money usd={l.unitPriceUsd} syp={l.unitPriceSyp} inline /></TableCell>
                        <TableCell align="left"><Money usd={lineTotals(l).usd} syp={lineTotals(l).syp} inline fontWeight={700} /></TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
              {creditWarning ? <Alert severity="warning">{creditWarning}</Alert> : null}
              <Alert severity="info">ستُحفظ الفاتورة كمسودة. عند ترحيلها تُرسل تلقائياً إلى دفتر الأستاذ (ERPNext) وتظهر حالتها في «مزامنة المحاسبة».</Alert>
            </Stack>
          ) : null}

          <Divider sx={{ my: 3 }} />
          <Stack direction="row" justifyContent="space-between">
            <Button sx={{ visibility: currentStep === 0 ? 'hidden' : 'visible' }} onClick={() => { setError(''); setCurrentStep((s) => Math.max(s - 1, 0)); }}>← السابق</Button>
            {currentStep < STEPS.length - 1 ? (
              <Button variant="contained" onClick={goNext}>التالي →</Button>
            ) : (
              <Button id="invoice-submit-btn" variant="contained" disabled={busy} onClick={() => void submit()}>{busy ? 'جارٍ الحفظ...' : '💾 حفظ الفاتورة (مسودة)'}</Button>
            )}
          </Stack>
        </CardContent>
      </Card>

      <ItemPickerModal
        open={pickerOpen}
        mode="sales"
        enforceStock={!isReturn}
        initialSearch={pickerSearch}
        title={isReturn ? 'اختيار أصناف المرتجع' : 'اختيار الأصناف للفاتورة'}
        onPick={addFromPick}
        onClose={() => setPickerOpen(false)}
      />
    </Box>
  );
}

function ReviewRow({ label, value }: { label: string; value: React.ReactNode }): JSX.Element {
  return (
    <Stack direction="row" justifyContent="space-between" gap={1.5}>
      <Typography variant="body2" color="text.secondary">{label}</Typography>
      <Typography variant="body2" fontWeight={600} component="div">{value}</Typography>
    </Stack>
  );
}

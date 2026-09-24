/**
 * A landed cost voucher (قيد رسملة مصاريف الشراء): the direct costs that follow a purchase — transport, customs, shipping,
 * insurance — gathered, split over the goods of one or more posted bills, and added to their cost. A draft is saved and shows what
 * posting would do (each line's share, the new unit cost, and per item how much raises the average cost of the stock on hand and how
 * much goes to cost of goods sold for what was already sold); posting books it, voiding takes it back.
 */
import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, IconButton, LinearProgress, MenuItem, Stack, Table, TableBody,
  TableCell, TableHead, TableRow, TextField, Tooltip, Typography,
} from '@mui/material';
import {
  CHARGE_LABEL, SPLIT_LABEL, purchasingApi, type LandedCostChargeInput, type LandedCostChargeType, type LandedCostDetail, type PurchaseInvoiceRow,
  type SplitMethod,
} from '../../api/endpoints/purchasing';
import { partiesApi } from '../../api/endpoints/parties';
import { unwrapNode, unwrapPaged } from '../../api/apiData';
import { extractApiError, toast } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';
import { formatQty } from '../../lib/format';
import { useChartAccounts } from '../../hooks/useChartAccounts';
import EntityPicker, { type PickerOption } from '../../components/pickers/EntityPicker';
import Money from '../../components/ui/Money';
import StatusChip from '../../components/ui/StatusChip';
import ReasonDialog from '../../components/ui/ReasonDialog';
import DocumentNavigator from '../../components/documents/DocumentNavigator';
import DeleteDocumentButton from '../../components/documents/DeleteDocumentButton';

type Props = {
  open: boolean;
  /** An existing voucher, or null for a new one. */
  voucherId: string | null;
  /** For a new voucher started from a bill. */
  initialBill?: PurchaseInvoiceRow | null;
  onClose: () => void;
  onChanged: () => void;
};

type ChargeRow = LandedCostChargeInput & { key: string; supplier: PickerOption | null; settlement: 'SUPPLIER' | 'ACCOUNT' };
type PartyRow = { id: string; displayName?: string; displayNameAr?: string; code?: string };

const today = (): string => new Date().toLocaleDateString('en-CA');
const newCharge = (): ChargeRow => ({
  key: crypto.randomUUID(), chargeType: 'FREIGHT', description: '', amountUsd: 0, splitMethod: 'VALUE', supplierPartyId: null, paidFromAccount: null,
  supplier: null, settlement: 'SUPPLIER',
});

export default function LandedCostDialog({ open, voucherId, initialBill, onClose, onChanged }: Props): JSX.Element {
  const [currentId, setCurrentId] = useState<string | null>(voucherId);
  const [detail, setDetail] = useState<LandedCostDetail | null>(null);
  const [date, setDate] = useState(today());
  const [notes, setNotes] = useState('');
  const [bills, setBills] = useState<Array<{ id: string; number: string }>>([]);
  const [charges, setCharges] = useState<ChargeRow[]>([newCharge()]);
  const [candidates, setCandidates] = useState<PurchaseInvoiceRow[]>([]);
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [voiding, setVoiding] = useState(false);
  const { ledgers } = useChartAccounts();
  const cashAccounts = useMemo(() => ledgers.filter((a) => a.accountType === 'Cash' || a.accountType === 'Bank'), [ledgers]);

  const load = useCallback(async (id: string): Promise<void> => {
    setLoading(true);
    try {
      const d = unwrapNode<LandedCostDetail>((await purchasingApi.getLandedCost(id)).data);
      if (!d) return;
      setDetail(d); setDate(d.voucher.voucherDate); setNotes(d.notes ?? '');
      setBills(d.bills.map((b) => ({ id: b.id, number: b.number })));
      setCharges(d.charges.map((c) => ({
        ...c, key: c.id, settlement: c.supplierPartyId ? 'SUPPLIER' : 'ACCOUNT',
        supplier: c.supplierPartyId ? { id: c.supplierPartyId, label: c.supplierName ?? '' } : null,
      })));
      setDirty(false);
    } catch (e: unknown) { toast.error(extractApiError(e, 'تعذر تحميل قيد الرسملة')); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => {
    if (!open) return;
    setCurrentId(voucherId); setDetail(null); setDirty(false);
    if (voucherId) { void load(voucherId); return; }
    setDate(today()); setNotes(''); setCharges([newCharge()]);
    setBills(initialBill ? [{ id: initialBill.id, number: initialBill.billNumber }] : []);
  }, [open, voucherId, initialBill, load]);

  useEffect(() => {
    if (!open) return;
    purchasingApi.listInvoices({ page: 1, pageSize: 200, status: 'POSTED' })
      .then((r) => setCandidates(unwrapPaged<PurchaseInvoiceRow>(r.data).items.filter((b) => b.kind === 'GOODS' && !b.isReturn)))
      .catch(() => setCandidates([]));
  }, [open]);

  const searchSuppliers = useCallback(async (text: string): Promise<PickerOption[]> => {
    const res = await partiesApi.getParties({ page: 1, pageSize: 10, typeCode: 'VENDOR', isActive: true, searchTerm: text || undefined });
    return unwrapPaged<PartyRow>(res.data).items.map((p) => ({ id: p.id, label: p.displayNameAr || p.displayName || p.id.slice(0, 8), sublabel: p.code }));
  }, []);

  const status = detail?.voucher.status ?? 'DRAFT';
  const editable = status === 'DRAFT';
  const total = charges.reduce((t, c) => t + (Number(c.amountUsd) || 0), 0);
  const patch = (key: string, change: Partial<ChargeRow>): void => {
    setCharges((all) => all.map((c) => (c.key === key ? { ...c, ...change } : c))); setDirty(true);
  };

  function problem(): string {
    if (bills.length === 0) return 'اختر فاتورة الشراء التي تحمل المصاريف';
    if (charges.length === 0) return 'أضف مصروفاً واحداً على الأقل';
    for (const c of charges) {
      if (!(Number(c.amountUsd) > 0)) return `أدخل مبلغ ${CHARGE_LABEL[c.chargeType]}`;
      if (c.settlement === 'SUPPLIER' && !c.supplier) return `اختر المورّد المستحق لمصروف ${CHARGE_LABEL[c.chargeType]}`;
      if (c.settlement === 'ACCOUNT' && !c.paidFromAccount) return `اختر الصندوق أو المصرف الذي دُفع منه ${CHARGE_LABEL[c.chargeType]}`;
    }
    return '';
  }

  async function save(): Promise<void> {
    const p = problem();
    if (p) { toast.error(p); return; }
    setBusy(true);
    try {
      const body = {
        voucherDate: date, fxRateId: detail?.fxRateId ?? null, notes: notes.trim() || null, purchaseInvoiceIds: bills.map((b) => b.id),
        charges: charges.map((c) => ({
          chargeType: c.chargeType, description: c.description?.trim() || null, amountUsd: Number(c.amountUsd), splitMethod: c.splitMethod,
          supplierPartyId: c.settlement === 'SUPPLIER' ? c.supplier?.id ?? null : null, paidFromAccount: c.settlement === 'ACCOUNT' ? c.paidFromAccount : null,
        })),
      };
      const id = currentId
        ? (await purchasingApi.updateLandedCost(currentId, body), currentId)
        : String(unwrapNode<string>((await purchasingApi.createLandedCost(body)).data) ?? '');
      toast.success('حُفظ القيد مسودةً — راجع التوزيع ثم رحّله');
      setCurrentId(id); await load(id); onChanged();
    } catch (e: unknown) { toast.error(extractApiError(e, 'تعذر حفظ قيد الرسملة')); }
    finally { setBusy(false); }
  }

  async function run(action: () => Promise<Parameters<typeof notifyResult>[0]>, done: string): Promise<void> {
    if (!currentId) return;
    setBusy(true);
    try { notifyResult(await action(), done); await load(currentId); onChanged(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر تنفيذ العملية')); }
    finally { setBusy(false); }
  }

  const chargeColumns = detail?.charges ?? [];

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="lg" scroll="paper">
      <DialogTitle>
        <Stack direction="row" gap={2} alignItems="center" flexWrap="wrap">
          <span>قيد رسملة مصاريف الشراء {detail ? detail.voucher.voucherNumber : '(جديد)'}</span>
          {detail ? <StatusChip status={detail.voucher.status} /> : null}
          {currentId ? <Box sx={{ mr: 'auto' }}><DocumentNavigator kind="landed-costs" id={currentId} onNavigate={(id) => { setCurrentId(id); void load(id); }} /></Box> : null}
        </Stack>
      </DialogTitle>
      <DialogContent dividers>
        {loading ? <LinearProgress sx={{ mb: 2 }} /> : null}
        {detail?.voidReason ? <Alert severity="warning" sx={{ mb: 2 }}>سبب الإلغاء: {detail.voidReason}</Alert> : null}

        <Stack direction="row" gap={2} flexWrap="wrap" sx={{ mb: 2 }}>
          <TextField size="small" type="date" label="تاريخ القيد" value={date} disabled={!editable} InputLabelProps={{ shrink: true }}
            onChange={(e) => { setDate(e.target.value); setDirty(true); }} />
          <TextField size="small" label="ملاحظات (رقم الشحنة، البيان الجمركي...)" value={notes} disabled={!editable} sx={{ flex: 1, minWidth: 260 }}
            onChange={(e) => { setNotes(e.target.value); setDirty(true); }} />
        </Stack>

        <Typography variant="subtitle2" fontWeight={800} sx={{ mb: 1 }}>فواتير الشراء التي تحمل المصاريف</Typography>
        <Stack direction="row" gap={1} flexWrap="wrap" alignItems="center" sx={{ mb: 2 }}>
          {bills.map((b) => (
            <Chip key={b.id} label={b.number} onDelete={editable ? () => { setBills(bills.filter((x) => x.id !== b.id)); setDirty(true); } : undefined} />
          ))}
          {editable ? (
            <TextField select size="small" label="إضافة فاتورة" value="" sx={{ minWidth: 260 }}
              onChange={(e) => {
                const b = candidates.find((x) => x.id === e.target.value);
                if (b && !bills.some((x) => x.id === b.id)) { setBills([...bills, { id: b.id, number: b.billNumber }]); setDirty(true); }
              }}>
              {candidates.filter((c) => !bills.some((b) => b.id === c.id)).map((c) => (
                <MenuItem key={c.id} value={c.id}>{c.billNumber} — {c.supplierName} — <Money usd={c.totalUsd} /></MenuItem>
              ))}
            </TextField>
          ) : null}
        </Stack>

        <Typography variant="subtitle2" fontWeight={800} sx={{ mb: 1 }}>المصاريف</Typography>
        <Box sx={{ overflowX: 'auto', mb: 1 }}>
          <Table size="small">
            <TableHead>
              <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
                <TableCell sx={{ minWidth: 120 }}>النوع</TableCell><TableCell sx={{ minWidth: 160 }}>البيان</TableCell><TableCell sx={{ minWidth: 110 }}>المبلغ $</TableCell>
                <TableCell sx={{ minWidth: 130 }}>التوزيع</TableCell><TableCell sx={{ minWidth: 150 }}>التسوية</TableCell><TableCell sx={{ minWidth: 240 }}>المورّد / الحساب</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {charges.map((c) => {
                const bill = detail?.charges.find((x) => x.id === c.key)?.serviceBill;
                return (
                  <TableRow key={c.key}>
                    <TableCell>
                      <TextField select size="small" fullWidth value={c.chargeType} disabled={!editable} onChange={(e) => patch(c.key, { chargeType: e.target.value as LandedCostChargeType })}>
                        {(Object.keys(CHARGE_LABEL) as LandedCostChargeType[]).map((t) => <MenuItem key={t} value={t}>{CHARGE_LABEL[t]}</MenuItem>)}
                      </TextField>
                    </TableCell>
                    <TableCell><TextField size="small" fullWidth value={c.description ?? ''} disabled={!editable} onChange={(e) => patch(c.key, { description: e.target.value })} /></TableCell>
                    <TableCell><TextField size="small" type="number" fullWidth value={c.amountUsd || ''} disabled={!editable} inputProps={{ min: 0, step: 'any' }} onChange={(e) => patch(c.key, { amountUsd: Number(e.target.value) })} /></TableCell>
                    <TableCell>
                      <TextField select size="small" fullWidth value={c.splitMethod} disabled={!editable} onChange={(e) => patch(c.key, { splitMethod: e.target.value as SplitMethod })}>
                        {(Object.keys(SPLIT_LABEL) as SplitMethod[]).map((m) => <MenuItem key={m} value={m}>{SPLIT_LABEL[m]}</MenuItem>)}
                      </TextField>
                    </TableCell>
                    <TableCell>
                      <TextField select size="small" fullWidth value={c.settlement} disabled={!editable} onChange={(e) => patch(c.key, { settlement: e.target.value as ChargeRow['settlement'] })}>
                        <MenuItem value="SUPPLIER">دين لمورّد (فاتورة خدمة)</MenuItem>
                        <MenuItem value="ACCOUNT">مدفوع من صندوق / مصرف</MenuItem>
                      </TextField>
                    </TableCell>
                    <TableCell>
                      {c.settlement === 'SUPPLIER' ? (
                        editable
                          ? <EntityPicker value={c.supplier} onChange={(v) => patch(c.key, { supplier: v })} search={searchSuppliers} placeholder="ابحث باسم المورّد..." />
                          : <Stack><Typography variant="body2">{c.supplier?.label}</Typography>{bill ? <Typography variant="caption" color="text.secondary">فاتورة الخدمة {bill.number}</Typography> : null}</Stack>
                      ) : cashAccounts.length > 0 ? (
                        <TextField select size="small" fullWidth value={c.paidFromAccount ?? ''} disabled={!editable} onChange={(e) => patch(c.key, { paidFromAccount: e.target.value })}>
                          {cashAccounts.map((a) => <MenuItem key={a.name} value={a.name}>{a.accountName}</MenuItem>)}
                        </TextField>
                      ) : (
                        <TextField size="small" fullWidth placeholder="اسم الحساب في ERPNext" value={c.paidFromAccount ?? ''} disabled={!editable} onChange={(e) => patch(c.key, { paidFromAccount: e.target.value })} />
                      )}
                    </TableCell>
                    <TableCell>
                      {editable && charges.length > 1 ? <Tooltip title="حذف السطر"><IconButton size="small" onClick={() => { setCharges(charges.filter((x) => x.key !== c.key)); setDirty(true); }}>✕</IconButton></Tooltip> : null}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </Box>
        <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 3 }}>
          {editable ? <Button size="small" onClick={() => { setCharges([...charges, newCharge()]); setDirty(true); }}>＋ مصروف</Button> : <span />}
          <Stack direction="row" gap={1} alignItems="center"><Typography variant="body2">مجموع المصاريف</Typography><Money usd={total} fontWeight={800} /></Stack>
        </Stack>

        {detail && !dirty ? (
          <>
            <Alert severity={detail.previewProblem ? 'error' : detail.isPreview ? 'info' : 'success'} sx={{ mb: 2 }}>
              {detail.previewProblem ?? (detail.isPreview
                ? 'معاينة: هذا ما سيحدث عند الترحيل بحسب المخزون والتكلفة الحاليين.'
                : 'هذا ما رُحِّل: التكلفة قبل وبعد، والجزء الذي حُمِّل على تكلفة المبيعات لما بيع من البضاعة.')}
            </Alert>
            <Typography variant="subtitle2" fontWeight={800} sx={{ mb: 1 }}>توزيع المصاريف على الأسطر</Typography>
            <Box sx={{ overflowX: 'auto', mb: 3 }}>
              <Table size="small">
                <TableHead>
                  <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
                    <TableCell>الفاتورة</TableCell><TableCell>الصنف</TableCell><TableCell align="left">الكمية</TableCell><TableCell align="left">تكلفة الوحدة</TableCell>
                    {chargeColumns.map((c) => <TableCell key={c.id} align="left">{CHARGE_LABEL[c.chargeType]}</TableCell>)}
                    <TableCell align="left">المجموع</TableCell><TableCell align="left">التكلفة الواصلة للوحدة</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {detail.lines.map((l) => (
                    <TableRow key={l.purchaseInvoiceLineId}>
                      <TableCell>{l.billNumber}</TableCell>
                      <TableCell><Typography component="span" sx={{ fontFamily: 'monospace', fontWeight: 700 }} color="primary">{l.itemCode}</Typography> {l.itemName}</TableCell>
                      <TableCell align="left">{formatQty(l.quantity)}</TableCell>
                      <TableCell align="left"><Money usd={l.netUnitCostUsd} /></TableCell>
                      {chargeColumns.map((c) => <TableCell key={c.id} align="left"><Money usd={l.byCharge[c.id] ?? 0} /></TableCell>)}
                      <TableCell align="left"><Money usd={l.allocatedUsd} fontWeight={700} /></TableCell>
                      <TableCell align="left"><Money usd={l.landedUnitCostUsd} fontWeight={800} color="primary.main" /></TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Box>
            <Typography variant="subtitle2" fontWeight={800} sx={{ mb: 1 }}>الأثر على تكلفة الأصناف</Typography>
            <Box sx={{ overflowX: 'auto' }}>
              <Table size="small">
                <TableHead>
                  <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
                    <TableCell>الصنف</TableCell><TableCell align="left">الكمية المشتراة</TableCell><TableCell align="left">الموجود حالياً</TableCell>
                    <TableCell align="left">المصاريف</TableCell><TableCell align="left">على المخزون</TableCell><TableCell align="left">على تكلفة المبيعات</TableCell>
                    <TableCell align="left">متوسط التكلفة قبل ← بعد</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {detail.effects.map((e) => (
                    <TableRow key={e.skuId}>
                      <TableCell><Typography component="span" sx={{ fontFamily: 'monospace', fontWeight: 700 }} color="primary">{e.itemCode}</Typography> {e.itemName}</TableCell>
                      <TableCell align="left">{formatQty(e.quantity)}</TableCell>
                      <TableCell align="left">{formatQty(e.onHand)}</TableCell>
                      <TableCell align="left"><Money usd={e.allocatedUsd} /></TableCell>
                      <TableCell align="left"><Money usd={e.capitalizedUsd} color="success.main" /></TableCell>
                      <TableCell align="left"><Money usd={e.expensedUsd} color={e.expensedUsd > 0 ? 'warning.main' : undefined} /></TableCell>
                      <TableCell align="left"><Money usd={e.costBeforeUsd} /> ← <Money usd={e.costAfterUsd} fontWeight={800} /></TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Box>
          </>
        ) : dirty && currentId ? <Alert severity="info">احفظ التغييرات لرؤية التوزيع المحدَّث.</Alert> : null}
      </DialogContent>
      <DialogActions>
        {currentId && detail ? <DeleteDocumentButton kind="landed-costs" id={currentId} number={detail.voucher.voucherNumber} status={status} onDeleted={() => { onChanged(); onClose(); }} /> : null}
        <Box sx={{ flex: 1 }} />
        <Button onClick={onClose}>إغلاق</Button>
        {editable ? <Button variant="outlined" disabled={busy} onClick={() => void save()}>حفظ مسودة</Button> : null}
        {editable && currentId ? (
          <Button variant="contained" color="success" disabled={busy || dirty || Boolean(detail?.previewProblem)}
            onClick={() => void run(() => purchasingApi.postLandedCost(currentId), 'رُحِّل القيد وأُضيفت المصاريف إلى تكلفة الأصناف')}>ترحيل</Button>
        ) : null}
        {status === 'POSTED' ? <Button color="error" disabled={busy} onClick={() => setVoiding(true)}>إلغاء القيد</Button> : null}
      </DialogActions>
      <ReasonDialog open={voiding} title="سبب إلغاء قيد الرسملة" confirmLabel="إلغاء القيد" minLength={5} onClose={() => setVoiding(false)}
        onConfirm={async (reason) => { setVoiding(false); await run(() => purchasingApi.voidLandedCost(currentId!, reason), 'أُلغي القيد وأُعيدت تكلفة الأصناف'); }} />
    </Dialog>
  );
}

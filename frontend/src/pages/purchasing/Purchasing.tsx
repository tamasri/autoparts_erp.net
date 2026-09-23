/** Purchasing: supplier bills (post = goods received, cost updated, sent to ERPNext) and supplier payments. */
import { ARABIC_PAGINATION } from '../../lib/tablePagination';
import { useCallback, useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Paper, Stack, Tab, Table, TableBody, TableCell, TableContainer,
  TableHead, TablePagination, TableRow, Tabs, TextField,
} from '@mui/material';
import { purchasingApi, type PurchaseInvoiceDetail, type PurchaseInvoiceRow, type SupplierPaymentRow } from '../../api/endpoints/purchasing';
import { unwrapNode, unwrapPaged } from '../../api/apiData';
import { extractApiError, toast } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';
import { num, ymd, type ExportDocument } from '../../lib/exportClient';
import PageHeader from '../../components/ui/PageHeader';
import ExportMenu from '../../components/ui/ExportMenu';
import DocumentViewButton from '../../components/ui/DocumentViewButton';
import PurchaseInvoiceDialog from '../../features/purchasing/PurchaseInvoiceDialog';
import SupplierPaymentDialog from '../../features/purchasing/SupplierPaymentDialog';

const STATUS: Record<string, { label: string; color: 'default' | 'success' | 'error' }> = {
  DRAFT: { label: 'مسودة', color: 'default' }, POSTED: { label: 'مرحّلة', color: 'success' }, VOID: { label: 'ملغاة', color: 'error' },
};
const FILTERS = [{ key: '', label: 'الكل' }, { key: 'DRAFT', label: 'مسودة' }, { key: 'POSTED', label: 'مرحّلة' }, { key: 'VOID', label: 'ملغاة' }];
const METHOD: Record<string, string> = { CASH: 'نقداً', USD_CASH: 'نقداً (دولار)', BANK_TRANSFER: 'حوالة', CHEQUE: 'شيك' };
const money = (v: number): string => v.toLocaleString('en-US', { maximumFractionDigits: 2 });

async function billDocument(id: string): Promise<ExportDocument> {
  const d = unwrapNode<PurchaseInvoiceDetail>((await purchasingApi.getInvoice(id)).data) as PurchaseInvoiceDetail;
  const i = d.invoice;
  return {
    title: `فاتورة شراء ${i.billNumber}`, subtitle: STATUS[i.status]?.label, fileName: `purchase-${i.billNumber}`,
    fields: [
      { label: 'المورّد', value: i.supplierName }, { label: 'رقم فاتورة المورّد', value: i.supplierRef }, { label: 'التاريخ', value: ymd(i.billDate) },
      { label: 'الاستحقاق', value: ymd(i.dueDate) }, { label: 'المدفوع ($)', value: money(i.paidUsd) }, { label: 'المتبقي ($)', value: money(i.balanceUsd) },
      ...(d.discountAmountUsd > 0
        ? [{ label: 'مجموع البنود ($)', value: money(d.subtotalUsd) }, { label: d.discountPct ? `خصم الفاتورة ${d.discountPct}% ($)` : 'خصم الفاتورة ($)', value: money(d.discountAmountUsd) }]
        : []),
    ],
    tables: [{
      title: 'الأصناف', columns: ['#', 'الرمز', 'الصنف', 'الكمية', 'التكلفة ($)', 'الخصم %', 'الإجمالي ($)'],
      rows: d.lines.map((l) => [String(l.lineNumber), l.itemCode, l.itemName, num(l.quantity), num(l.unitCostUsd), num(l.discountPct), num(l.lineTotalUsd)]),
      totals: ['', '', 'الإجمالي', '', '', '', num(i.totalUsd)], numericColumns: [3, 4, 5, 6],
    }],
    footer: d.notes ?? undefined,
  };
}

function BillsTab(): JSX.Element {
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(20);
  const [rows, setRows] = useState<PurchaseInvoiceRow[]>([]);
  const [total, setTotal] = useState(0);
  const [error, setError] = useState('');
  const [creating, setCreating] = useState(false);
  const [paying, setPaying] = useState<{ id: string; name: string } | null>(null);
  const [voiding, setVoiding] = useState<PurchaseInvoiceRow | null>(null);
  const [reason, setReason] = useState('');

  useEffect(() => { const h = window.setTimeout(() => { setQuery(search.trim()); setPage(0); }, 350); return () => window.clearTimeout(h); }, [search]);

  const load = useCallback(async () => {
    try {
      const data = unwrapPaged<PurchaseInvoiceRow>((await purchasingApi.listInvoices({ page: page + 1, pageSize, status: status || undefined, search: query || undefined })).data);
      setRows(data.items); setTotal(data.totalCount); setError('');
    } catch (e: unknown) { setError(extractApiError(e, 'تعذر تحميل فواتير الشراء')); }
  }, [page, pageSize, status, query]);

  useEffect(() => { void load(); }, [load]);

  async function post(id: string): Promise<void> {
    try { notifyResult(await purchasingApi.postInvoice(id), 'تم ترحيل الفاتورة واستلام البضاعة'); await load(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر ترحيل الفاتورة')); }
  }

  async function doVoid(): Promise<void> {
    if (!voiding) return;
    if (reason.trim().length < 5) { toast.error('اكتب سبب الإلغاء (5 أحرف على الأقل)'); return; }
    try { notifyResult(await purchasingApi.voidInvoice(voiding.id, reason.trim()), 'تم إلغاء الفاتورة وإخراج البضاعة'); setVoiding(null); setReason(''); await load(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر إلغاء الفاتورة')); }
  }

  const buildExport = async (): Promise<ExportDocument> => {
    const data = unwrapPaged<PurchaseInvoiceRow>((await purchasingApi.listInvoices({ page: 1, pageSize: 200, status: status || undefined, search: query || undefined })).data);
    return {
      title: 'فواتير الشراء', subtitle: `${data.totalCount} فاتورة`, fileName: 'purchase-invoices', fields: [],
      tables: [{
        columns: ['الرقم', 'المورّد', 'رقم المورّد', 'التاريخ', 'الاستحقاق', 'الإجمالي ($)', 'المدفوع ($)', 'المتبقي ($)', 'الحالة'],
        rows: data.items.map((b) => [b.billNumber, b.supplierName, b.supplierRef ?? '', ymd(b.billDate), ymd(b.dueDate), num(b.totalUsd), num(b.paidUsd), num(b.balanceUsd), STATUS[b.status]?.label ?? b.status]),
        numericColumns: [5, 6, 7],
      }],
    };
  };

  return (
    <Box>
      <Stack direction="row" gap={1} alignItems="center" flexWrap="wrap" sx={{ mb: 2 }}>
        <Tabs value={FILTERS.findIndex((f) => f.key === status)} onChange={(_, i: number) => { setStatus(FILTERS[i].key); setPage(0); }}>{FILTERS.map((f) => <Tab key={f.key} label={f.label} />)}</Tabs>
        <TextField size="small" placeholder="بحث بالرقم أو المورّد..." value={search} onChange={(e) => setSearch(e.target.value)} sx={{ width: 260, mr: 'auto' }} />
        <ExportMenu build={buildExport} />
        <Button variant="contained" size="small" onClick={() => setCreating(true)}>＋ فاتورة شراء</Button>
      </Stack>
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 3 }}>
        <Table size="small">
          <TableHead><TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
            <TableCell>الرقم</TableCell><TableCell>المورّد</TableCell><TableCell>التاريخ</TableCell><TableCell>الاستحقاق</TableCell>
            <TableCell align="left">الإجمالي ($)</TableCell><TableCell align="left">المتبقي ($)</TableCell><TableCell>الحالة</TableCell><TableCell />
          </TableRow></TableHead>
          <TableBody>
            {rows.length === 0 ? <TableRow><TableCell colSpan={8} align="center" sx={{ py: 5, color: 'text.secondary' }}>لا توجد فواتير شراء</TableCell></TableRow> : null}
            {rows.map((b) => (
              <TableRow key={b.id} hover>
                <TableCell sx={{ fontWeight: 700 }}>{b.billNumber}{b.supplierRef ? <Box component="span" sx={{ mx: 1, color: 'text.secondary', fontSize: 12 }}>({b.supplierRef})</Box> : null}</TableCell>
                <TableCell>{b.supplierName}</TableCell><TableCell>{ymd(b.billDate)}</TableCell><TableCell>{ymd(b.dueDate)}</TableCell>
                <TableCell align="left">{money(b.totalUsd)}</TableCell>
                <TableCell align="left" sx={{ fontWeight: 700, color: b.balanceUsd > 0 ? 'error.main' : 'text.secondary' }}>{money(b.balanceUsd)}</TableCell>
                <TableCell><Chip size="small" color={STATUS[b.status]?.color} label={STATUS[b.status]?.label ?? b.status} variant="outlined" /></TableCell>
                <TableCell align="left" sx={{ whiteSpace: 'nowrap' }}>
                  <DocumentViewButton load={() => billDocument(b.id)} />
                  {b.status === 'DRAFT' ? <Button size="small" onClick={() => void post(b.id)}>ترحيل واستلام</Button> : null}
                  {b.status === 'POSTED' && b.balanceUsd > 0 ? <Button size="small" onClick={() => setPaying({ id: b.supplierPartyId, name: b.supplierName })}>دفع</Button> : null}
                  {b.status !== 'VOID' ? <Button size="small" color="error" onClick={() => { setVoiding(b); setReason(''); }}>إلغاء</Button> : null}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        <TablePagination component="div" count={total} page={page} rowsPerPage={pageSize} rowsPerPageOptions={[10, 20, 50, 100]}
          onPageChange={(_, p) => setPage(p)} onRowsPerPageChange={(e) => { setPageSize(Number(e.target.value)); setPage(0); }} {...ARABIC_PAGINATION} />
      </TableContainer>

      <PurchaseInvoiceDialog open={creating} onClose={() => setCreating(false)} onSaved={() => void load()} />
      <SupplierPaymentDialog open={paying !== null} supplier={paying} onClose={() => setPaying(null)} onSaved={() => void load()} />
      <Dialog open={voiding !== null} onClose={() => setVoiding(null)} fullWidth maxWidth="xs">
        <DialogTitle>إلغاء {voiding?.billNumber}</DialogTitle>
        <DialogContent>
          <Alert severity="warning" sx={{ mb: 2 }}>{voiding?.status === 'POSTED' ? 'ستخرج البضاعة المستلَمة من المخزون ويُلغى المستند في ERPNext. لا يُسمح بذلك إن بيع جزء منها أو وُجدت دفعات.' : 'ستُلغى المسودة.'}</Alert>
          <TextField fullWidth size="small" label="السبب *" value={reason} onChange={(e) => setReason(e.target.value)} />
        </DialogContent>
        <DialogActions><Button onClick={() => setVoiding(null)}>رجوع</Button><Button color="error" variant="contained" onClick={() => void doVoid()}>تأكيد الإلغاء</Button></DialogActions>
      </Dialog>
    </Box>
  );
}

function PaymentsTab(): JSX.Element {
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(20);
  const [rows, setRows] = useState<SupplierPaymentRow[]>([]);
  const [total, setTotal] = useState(0);
  const [error, setError] = useState('');
  const [reversing, setReversing] = useState<SupplierPaymentRow | null>(null);
  const [reason, setReason] = useState('');

  const load = useCallback(async () => {
    try {
      const data = unwrapPaged<SupplierPaymentRow>((await purchasingApi.listPayments({ page: page + 1, pageSize })).data);
      setRows(data.items); setTotal(data.totalCount); setError('');
    } catch (e: unknown) { setError(extractApiError(e, 'تعذر تحميل مدفوعات الموردين')); }
  }, [page, pageSize]);

  useEffect(() => { void load(); }, [load]);

  async function reverse(): Promise<void> {
    if (!reversing) return;
    if (reason.trim().length < 5) { toast.error('اكتب سبب العكس (5 أحرف على الأقل)'); return; }
    try { notifyResult(await purchasingApi.reversePayment(reversing.id, reason.trim()), 'تم عكس الدفعة وإعادة المبلغ على الفواتير'); setReversing(null); setReason(''); await load(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر عكس الدفعة')); }
  }

  return (
    <Box>
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 3 }}>
        <Table size="small">
          <TableHead><TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
            <TableCell>رقم السند</TableCell><TableCell>المورّد</TableCell><TableCell>التاريخ</TableCell><TableCell>الطريقة</TableCell>
            <TableCell align="left">المبلغ ($)</TableCell><TableCell>الحالة</TableCell><TableCell />
          </TableRow></TableHead>
          <TableBody>
            {rows.length === 0 ? <TableRow><TableCell colSpan={7} align="center" sx={{ py: 5, color: 'text.secondary' }}>لا توجد مدفوعات</TableCell></TableRow> : null}
            {rows.map((p) => (
              <TableRow key={p.id} hover sx={{ opacity: p.isReversed ? 0.55 : 1 }}>
                <TableCell sx={{ fontWeight: 700 }}>{p.paymentNumber}</TableCell><TableCell>{p.supplierName}</TableCell><TableCell>{ymd(p.paymentDate)}</TableCell>
                <TableCell>{METHOD[p.paymentMethod] ?? p.paymentMethod}</TableCell><TableCell align="left" sx={{ fontWeight: 700 }}>{money(p.amountUsd)}</TableCell>
                <TableCell><Chip size="small" variant="outlined" color={p.isReversed ? 'error' : 'success'} label={p.isReversed ? 'معكوسة' : 'فعّالة'} /></TableCell>
                <TableCell align="left">{!p.isReversed ? <Button size="small" onClick={() => { setReversing(p); setReason(''); }}>↩ عكس</Button> : null}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        <TablePagination component="div" count={total} page={page} rowsPerPage={pageSize} rowsPerPageOptions={[10, 20, 50, 100]}
          onPageChange={(_, p) => setPage(p)} onRowsPerPageChange={(e) => { setPageSize(Number(e.target.value)); setPage(0); }} {...ARABIC_PAGINATION} />
      </TableContainer>
      <Dialog open={reversing !== null} onClose={() => setReversing(null)} fullWidth maxWidth="xs">
        <DialogTitle>عكس {reversing?.paymentNumber}</DialogTitle>
        <DialogContent><TextField fullWidth size="small" label="السبب *" value={reason} onChange={(e) => setReason(e.target.value)} sx={{ mt: 1 }} /></DialogContent>
        <DialogActions><Button onClick={() => setReversing(null)}>رجوع</Button><Button color="error" variant="contained" onClick={() => void reverse()}>تأكيد العكس</Button></DialogActions>
      </Dialog>
    </Box>
  );
}

export default function Purchasing(): JSX.Element {
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') === 'payments' ? 1 : 0;
  return (
    <Box>
      <PageHeader title="المشتريات" subtitle="فواتير الموردين (الترحيل يستلم البضاعة ويحدّث التكلفة ويرسل إلى ERPNext) ومدفوعاتهم" />
      <Tabs value={tab} onChange={(_, i: number) => setParams(i === 1 ? { tab: 'payments' } : {})} sx={{ mb: 2 }}>
        <Tab label="فواتير الشراء" /><Tab label="مدفوعات الموردين" />
      </Tabs>
      {tab === 0 ? <BillsTab /> : <PaymentsTab />}
    </Box>
  );
}

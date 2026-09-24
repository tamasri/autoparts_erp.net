/**
 * One sales invoice or return: lines, how the total is made up, what is paid or credited by returns, the draft → confirmed → posted
 * steps, and the returns made against a sale (a return is always made from its sale, at the sale's prices).
 */
import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import {
  Alert, Box, Button, Card, CardContent, Chip, Divider, LinearProgress, Link, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableContainer,
  TableHead, TableRow, TextField, Typography,
} from '@mui/material';
import { invoicesApi, type InvoiceAmounts } from '../../api/endpoints/invoices';
import type { CreateReturn, DocumentLink, ReturnableLine } from '../../api/endpoints/returns';
import { unwrapList, unwrapNode } from '../../api/apiData';
import { useCan } from '../../hooks/useCan';
import ReturnDialog from '../../features/returns/ReturnDialog';
import { toast, extractApiError } from '../../lib/toast';
import { formatPct, formatQty } from '../../lib/format';
import PageHeader from '../../components/ui/PageHeader';
import StatusChip from '../../components/ui/StatusChip';
import ReasonDialog from '../../components/ui/ReasonDialog';
import Money from '../../components/ui/Money';
import DocumentNavigator from '../../components/documents/DocumentNavigator';
import DeleteDocumentButton from '../../components/documents/DeleteDocumentButton';
import DocumentDialog from '../../components/ui/DocumentDialog';
import { endpointPdf, type ExportDocument } from '../../lib/exportClient';

type InvoiceLine = {
  id: string; lineNumber: number; skuCode: string; skuName: string; quantity: number;
  unitPriceSyp: number; unitPriceUsd: number; discountPct: number; lineTotalSyp: number; lineTotalUsd: number; isPriceOverride: boolean;
};

type Invoice = {
  id: string; invoiceNumber: string; status: string; type: string; customerId: string; customerCode: string; customerName: string;
  invoiceDate: string; dueDate: string; totalSyp: number; totalUsd: number; paidSyp: number; paidUsd: number; balanceSyp: number; balanceUsd: number;
  dueDateDisplay: string; totalSypInWords: string; totalUsdInWords: string; lines: InvoiceLine[]; amounts: InvoiceAmounts;
  /** Credit of returns applied to this sale (+), or given by this return to its sale (−). */
  creditAppliedSyp: number; creditAppliedUsd: number;
  returnOf: DocumentLink | null; returns: DocumentLink[];
};

function Row({ label, children, strong }: { label: string; children: React.ReactNode; strong?: boolean }): JSX.Element {
  return (
    <Stack direction="row" justifyContent="space-between" alignItems="center" gap={2}>
      <Typography variant="body2" color={strong ? 'text.primary' : 'text.secondary'} fontWeight={strong ? 700 : 400}>{label}</Typography>
      {children}
    </Stack>
  );
}

export default function InvoiceDetail(): JSX.Element {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const [invoice, setInvoice] = useState<Invoice | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [voiding, setVoiding] = useState(false);
  const [returning, setReturning] = useState(false);
  const [printing, setPrinting] = useState(false);
  const canReturn = useCan('invoices:create');
  const loadReturnable = useCallback(async (): Promise<ReturnableLine[]> => unwrapList<ReturnableLine>((await invoicesApi.returnable(id)).data), [id]);
  const createReturn = useCallback(async (body: CreateReturn): Promise<string> => unwrapNode<{ id: string }>((await invoicesApi.createReturn(id, body)).data)?.id ?? '', [id]);
  const [discountMode, setDiscountMode] = useState<'pct' | 'usd'>('pct');
  const [discountValue, setDiscountValue] = useState('');

  const load = useCallback(async (): Promise<void> => {
    setLoading(true); setError('');
    try { setInvoice(unwrapNode<Invoice>((await invoicesApi.getInvoiceById(id)).data)); }
    catch (e: unknown) { setError(extractApiError(e, 'تعذر تحميل تفاصيل الفاتورة')); }
    finally { setLoading(false); }
  }, [id]);

  useEffect(() => { void load(); }, [load]);

  async function run(action: () => Promise<unknown>, done: string, fail: string): Promise<void> {
    setBusy(true);
    try { await action(); toast.success(done); await load(); }
    catch (e: unknown) { toast.error(extractApiError(e, fail)); }
    finally { setBusy(false); }
  }

  async function saveDiscount(): Promise<void> {
    const v = Number(discountValue || 0);
    await run(
      () => invoicesApi.setDiscount(id, discountMode === 'pct' ? { discountPct: v > 0 ? v : null } : { discountAmountUsd: v > 0 ? v : null }),
      v > 0 ? 'تم تطبيق خصم الفاتورة' : 'أُزيل خصم الفاتورة', 'تعذر تطبيق الخصم');
    setDiscountValue('');
  }

  // The printed invoice is the server's form (letterhead, totals, terms, recipient box, payment details, QR codes).
  const invoicePdf = useMemo(() => endpointPdf(() => invoicesApi.getPdf(id)), [id]);
  const printDoc = useMemo<ExportDocument | null>(
    () => (invoice
      ? { title: `${invoice.type.toUpperCase() === 'RETURN' ? 'مرتجع' : 'فاتورة'} ${invoice.invoiceNumber || ''}`, subtitle: invoice.customerName, fields: [], tables: [], fileName: `invoice-${invoice.invoiceNumber || id}` }
      : null),
    [invoice, id],
  );

  if (loading && !invoice) return <LinearProgress />;
  if (!invoice) return <Stack spacing={2}><Alert severity="error">{error || 'الفاتورة غير موجودة'}</Alert><Link component={RouterLink} to="/invoices">← الفواتير</Link></Stack>;

  const status = invoice.status.toUpperCase();
  const isReturn = invoice.type.toUpperCase() === 'RETURN';
  const a = invoice.amounts;
  const abs = (v: number): number => Math.abs(v);

  return (
    <Box>
      <PageHeader
        title={`${isReturn ? 'مرتجع' : 'فاتورة'} ${invoice.invoiceNumber || ''}`}
        subtitle={`${invoice.customerName} · ${invoice.invoiceDate}${invoice.returnOf ? ` · مرتجع الفاتورة ${invoice.returnOf.number}` : ''}`}
        crumbs={[{ label: 'الفواتير', to: '/invoices' }, { label: invoice.invoiceNumber || invoice.id.slice(0, 8) }]}
        actions={(
          <Stack direction="row" gap={1} flexWrap="wrap" alignItems="center">
            <DocumentNavigator kind="invoices" id={id} onNavigate={(next) => navigate(`/invoices/${next}`)} />
            <Button variant="outlined" disabled={busy} onClick={() => setPrinting(true)}>👁 عرض / طباعة</Button>
            {status === 'DRAFT' ? <Button variant="contained" disabled={busy} onClick={() => void run(() => invoicesApi.confirm(id), 'تم تأكيد الفاتورة', 'تعذر تأكيد الفاتورة')}>✓ تأكيد</Button> : null}
            {status === 'CONFIRMED' ? <Button variant="contained" color="success" disabled={busy} onClick={() => void run(() => invoicesApi.post(id), 'تم ترحيل الفاتورة', 'تعذر ترحيل الفاتورة')}>✓ ترحيل</Button> : null}
            {status === 'POSTED' && !isReturn && canReturn ? <Button variant="outlined" color="warning" disabled={busy} onClick={() => setReturning(true)}>↩ مرتجع</Button> : null}
            {status === 'POSTED' ? <Button color="error" disabled={busy} onClick={() => setVoiding(true)}>✕ إلغاء</Button> : null}
            <DeleteDocumentButton kind="invoices" id={id} number={invoice.invoiceNumber} status={status} size="medium" onDeleted={() => navigate('/invoices')} />
          </Stack>
        )}
      />
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      {invoice.returnOf ? (
        <Alert severity="info" sx={{ mb: 2 }}>
          مرتجع من الفاتورة <Link component={RouterLink} to={`/invoices/${invoice.returnOf.id}`} fontWeight={700}>{invoice.returnOf.number}</Link> — بأسعارها وتكلفتها، ويُطبَّق رصيده على ما تبقّى منها.
        </Alert>
      ) : null}
      {invoice.returns.length > 0 ? (
        <Stack direction="row" gap={1} alignItems="center" flexWrap="wrap" sx={{ mb: 2 }}>
          <Typography variant="body2" color="text.secondary">المرتجعات:</Typography>
          {invoice.returns.map((r) => (
            <Chip key={r.id} size="small" component={RouterLink} to={`/invoices/${r.id}`} clickable variant="outlined"
              color={r.status === 'POSTED' ? 'warning' : 'default'} label={`${r.number} · ${r.status === 'POSTED' ? 'مرحّل' : r.status === 'VOID' ? 'ملغى' : 'مسودة'}`} />
          ))}
        </Stack>
      ) : null}

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '2fr 1fr' }, gap: 3 }}>
        <Stack spacing={2}>
          <Card variant="outlined" sx={{ borderRadius: 3 }}>
            <CardContent>
              <Stack direction="row" gap={3} flexWrap="wrap" alignItems="center">
                <Box>
                  <Typography variant="caption" color="text.secondary">الزبون</Typography>
                  <Link component={RouterLink} to={`/customers/${invoice.customerId}`} display="block" fontWeight={700}>{invoice.customerName}</Link>
                  <Typography variant="caption" color="text.secondary">{invoice.customerCode}</Typography>
                </Box>
                <Box><Typography variant="caption" color="text.secondary">التاريخ</Typography><Typography fontWeight={600}>{invoice.invoiceDate}</Typography></Box>
                <Box><Typography variant="caption" color="text.secondary">الاستحقاق</Typography><Typography fontWeight={600}>{invoice.dueDate}</Typography><Typography variant="caption" color="text.secondary">{invoice.dueDateDisplay}</Typography></Box>
                <Box sx={{ mr: 'auto' }}><StatusChip status={invoice.status} />{isReturn ? <Chip size="small" color="warning" label="مرتجع" sx={{ mx: 1 }} /> : null}</Box>
              </Stack>
            </CardContent>
          </Card>

          <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 3 }}>
            <Table size="small">
              <TableHead>
                <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
                  <TableCell>#</TableCell><TableCell>الصنف</TableCell><TableCell align="left">الكمية</TableCell><TableCell align="left">سعر الوحدة</TableCell>
                  <TableCell align="left">الخصم</TableCell><TableCell align="left">الإجمالي</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {invoice.lines.length === 0 ? (
                  <TableRow><TableCell colSpan={6} align="center" sx={{ py: 4, color: 'text.secondary' }}>لا توجد أسطر</TableCell></TableRow>
                ) : invoice.lines.map((l) => (
                  <TableRow key={l.id}>
                    <TableCell>{l.lineNumber}</TableCell>
                    <TableCell>
                      <Typography component="span" sx={{ fontFamily: 'monospace', fontWeight: 700 }} color="primary">{l.skuCode}</Typography> {l.skuName}
                      {l.isPriceOverride ? <Chip size="small" color="warning" variant="outlined" label="سعر مُتجاوَز" sx={{ mx: 1 }} /> : null}
                    </TableCell>
                    <TableCell align="left">{formatQty(l.quantity)}</TableCell>
                    <TableCell align="left"><Money usd={l.unitPriceUsd} syp={l.unitPriceSyp} /></TableCell>
                    <TableCell align="left">{l.discountPct > 0 ? <Chip size="small" color="warning" label={formatPct(l.discountPct)} /> : '—'}</TableCell>
                    <TableCell align="left"><Money usd={l.lineTotalUsd} syp={l.lineTotalSyp} fontWeight={700} /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        </Stack>

        <Card variant="outlined" sx={{ borderRadius: 3, alignSelf: 'start' }}>
          <CardContent>
            <Stack spacing={1.2}>
              <Row label="مجموع البنود"><Money usd={abs(a.subtotalUsd)} syp={abs(a.subtotalSyp)} /></Row>
              {a.discountAmountUsd > 0 ? (
                <Row label={`خصم الفاتورة${a.discountPct ? ` ${formatPct(a.discountPct)}` : ''}`}><Money usd={-a.discountAmountUsd} syp={-a.discountAmountSyp} color="error.main" /></Row>
              ) : null}
              {a.deliveryFeeUsd > 0 || a.deliveryFeeSyp > 0 ? <Row label="أجور التوصيل"><Money usd={a.deliveryFeeUsd} syp={a.deliveryFeeSyp} /></Row> : null}
              <Divider />
              <Row label="الإجمالي" strong><Money usd={invoice.totalUsd} syp={invoice.totalSyp} variant="h6" fontWeight={800} color="primary.main" /></Row>
              <Typography variant="caption" color="text.secondary">{invoice.totalSypInWords}</Typography>
              <Divider />
              <Row label="المدفوع"><Money usd={invoice.paidUsd} syp={invoice.paidSyp} color="success.main" /></Row>
              {invoice.creditAppliedUsd !== 0 ? (
                <Row label={isReturn ? 'طُبِّق على الفاتورة الأصلية' : 'مرتجعات مطبّقة'}>
                  <Money usd={Math.abs(invoice.creditAppliedUsd)} syp={Math.abs(invoice.creditAppliedSyp)} color="warning.main" />
                </Row>
              ) : null}
              <Row label={invoice.balanceUsd < 0 ? 'رصيد دائن للزبون' : 'المتبقي'} strong>
                <Money usd={Math.abs(invoice.balanceUsd)} syp={Math.abs(invoice.balanceSyp)} fontWeight={700} color={invoice.balanceUsd > 0 ? 'error.main' : invoice.balanceUsd < 0 ? 'success.main' : undefined} />
              </Row>
              {status === 'DRAFT' ? (
                <>
                  <Divider />
                  <Typography variant="caption" color="text.secondary">خصم على الفاتورة (المسودة فقط؛ صفر يزيل الخصم)</Typography>
                  <Stack direction="row" gap={1}>
                    <TextField select size="small" value={discountMode} onChange={(e) => setDiscountMode(e.target.value as 'pct' | 'usd')} sx={{ width: 100 }}>
                      <MenuItem value="pct">%</MenuItem><MenuItem value="usd">$</MenuItem>
                    </TextField>
                    <TextField size="small" type="number" inputProps={{ min: 0 }} placeholder="0" value={discountValue} onChange={(e) => setDiscountValue(e.target.value)} sx={{ flex: 1 }} />
                    <Button variant="outlined" disabled={busy} onClick={() => void saveDiscount()}>تطبيق</Button>
                  </Stack>
                </>
              ) : null}
            </Stack>
          </CardContent>
        </Card>
      </Box>

      <DocumentDialog open={printing} onClose={() => setPrinting(false)} document={printDoc} pdf={invoicePdf} />
      <ReturnDialog open={returning} title={`مرتجع مبيعات من الفاتورة ${invoice.invoiceNumber}`} chooseLocation load={loadReturnable} submit={createReturn}
        onClose={() => setReturning(false)} onCreated={(newId) => { setReturning(false); navigate(`/invoices/${newId}`); }} />

      <ReasonDialog open={voiding} title="سبب إلغاء الفاتورة" confirmLabel="إلغاء الفاتورة" minLength={3}
        onClose={() => setVoiding(false)}
        onConfirm={async (reason) => { setVoiding(false); await run(() => invoicesApi.void(id, reason), 'تم إلغاء الفاتورة', 'تعذر إلغاء الفاتورة'); }} />
    </Box>
  );
}

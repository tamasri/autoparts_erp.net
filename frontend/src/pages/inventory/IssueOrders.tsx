import { useCallback, useState } from 'react';
import {
  Alert, Box, Button, Card, CardContent, Dialog, DialogActions, DialogContent, DialogTitle, LinearProgress, MenuItem, Paper, Stack, Table, TableBody,
  TableCell, TableHead, TableRow, TextField, Typography,
} from '@mui/material';
import PageHeader from '../../components/ui/PageHeader';
import DataTable from '../../components/ui/DataTable';
import StatusChip from '../../components/ui/StatusChip';
import { formatQty } from '../../lib/format';
import { issueOrdersApi } from '../../api/endpoints/issueOrders';
import { unwrapNode } from '../../api/apiData';
import { usePagedList } from '../../hooks/usePagedList';
import { useLocationNames } from '../../hooks/useLocationNames';
import { toast, extractApiError } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';
import DocumentViewButton from '../../components/ui/DocumentViewButton';
import { issueOrderDocument } from '../../lib/wmsDocuments';
import LocationSelect from '../../components/pickers/LocationSelect';
import WmsLinesEditor, { type WmsLine } from '../../components/wms/WmsLinesEditor';

type IssueOrder = { id: string; orderNo: string; sourceType: string; warehouseId: string; status: string; issuedAt?: string };
type OrderLine = { id: string; itemCode: string; itemName: string; requestedQty: number; pickedQty: number; verifiedQty: number; issuedQty: number };
type PickTask = { id: string; itemCode: string; itemName: string; locationCode: string; qty: number; status: string };
type OrderDetail = { order: IssueOrder; lines: OrderLine[]; pickTasks: PickTask[] };

const SOURCES = [
  { value: 'MANUAL', label: 'يدوي' },
  { value: 'SALES_ORDER', label: 'أمر بيع' },
  { value: 'TRANSFER', label: 'تحويل' },
];

export default function IssueOrders(): JSX.Element {
  const names = useLocationNames();
  const list = usePagedList<IssueOrder>({
    errorMessage: 'تعذر تحميل أوامر الصرف',
    fetcher: ({ page, pageSize }) => issueOrdersApi.list(page, pageSize),
  });

  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState('');
  const [formError, setFormError] = useState('');
  const [sourceType, setSourceType] = useState('MANUAL');
  const [warehouseId, setWarehouseId] = useState('');
  const [lines, setLines] = useState<WmsLine[]>([]);

  const [openId, setOpenId] = useState('');
  const [detail, setDetail] = useState<OrderDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [actionError, setActionError] = useState('');

  const loadDetail = useCallback(async (id: string): Promise<void> => {
    setDetailLoading(true); setActionError('');
    try { const res = await issueOrdersApi.get(id); setDetail(unwrapNode<OrderDetail>(res.data)); }
    catch (e: unknown) { setActionError(extractApiError(e, 'تعذر تحميل تفاصيل الأمر')); }
    finally { setDetailLoading(false); }
  }, []);

  function toggle(id: string): void {
    if (openId === id) { setOpenId(''); return; }
    setOpenId(id);
    void loadDetail(id);
  }

  async function create(): Promise<void> {
    if (!warehouseId) { setFormError('اختر المستودع'); return; }
    const valid = lines.filter((l) => Number(l.qty) > 0);
    if (valid.length === 0) { setFormError('أضف صنفاً واحداً على الأقل بكمية صحيحة'); return; }
    const over = valid.find((l) => Number(l.qty) > (l.item.stock.find((s) => s.locationId === l.locationId)?.available ?? 0));
    if (over) { setFormError(`الكمية المطلوبة تتجاوز المتاح للصنف ${over.code}`); return; }
    setBusy('create'); setFormError('');
    try {
      await issueOrdersApi.create({
        sourceType,
        warehouseId,
        lines: valid.map((l) => ({ itemId: l.itemId, requestedQty: Number(l.qty), sourceLocationId: l.locationId })),
        idempotencyKey: crypto.randomUUID(),
      });
      toast.success('تم إنشاء أمر الصرف');
      setWarehouseId(''); setLines([]); setShowForm(false); list.reload();
    } catch (e: unknown) { setFormError(extractApiError(e, 'تعذر إنشاء أمر الصرف')); }
    finally { setBusy(''); }
  }

  async function step(id: string, run: () => Promise<{ data: unknown }>, done: string, fail: string): Promise<void> {
    setBusy(id); setActionError('');
    try {
      const res = await run();
      notifyResult(res as never, done);
      await loadDetail(id);
      list.reload();
    } catch (e: unknown) { setActionError(extractApiError(e, fail)); }
    finally { setBusy(''); }
  }

  return (
    <Box>
      <PageHeader
        title="أوامر الصرف" subtitle="أمر ← مهام سحب ← تحقق ← صرف من المخزون"
        actions={<Button variant={showForm ? 'outlined' : 'contained'} onClick={() => setShowForm((s) => !s)}>{showForm ? '✕ إلغاء' : '＋ أمر صرف'}</Button>}
      />
      {list.error ? <Alert severity="error" sx={{ mb: 2 }}>{list.error}</Alert> : null}

      {showForm ? (
        <Card variant="outlined" sx={{ borderRadius: 3, mb: 3 }}>
          <CardContent>
            <Typography variant="h6" fontWeight={700} sx={{ mb: 2 }}>أمر صرف جديد</Typography>
            {formError ? <Alert severity="error" sx={{ mb: 2 }}>{formError}</Alert> : null}
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 2, mb: 2 }}>
              <TextField select size="small" label="نوع المصدر" value={sourceType} onChange={(e) => setSourceType(e.target.value)}>
                {SOURCES.map((s) => <MenuItem key={s.value} value={s.value}>{s.label}</MenuItem>)}
              </TextField>
              <LocationSelect type="WAREHOUSE" label="المستودع *" value={warehouseId} onChange={(id) => setWarehouseId(id)} />
            </Box>
            <WmsLinesEditor lines={lines} onChange={setLines} warehouseId={warehouseId} locationLabel="السحب من موقع" qtyLabel="الكمية المطلوبة" showAvailable pickerTitle="اختيار الأصناف المراد صرفها" />
            <Button variant="contained" sx={{ mt: 2 }} disabled={busy === 'create'} onClick={() => void create()}>💾 حفظ الأمر</Button>
          </CardContent>
        </Card>
      ) : null}

      <DataTable
        rows={list.items} getKey={(o) => o.id} loading={list.loading} empty="لا توجد أوامر صرف"
        paging={{ page: list.page - 1, pageSize: list.pageSize, total: list.totalCount, onPage: (p) => list.setPage(p + 1), onPageSize: list.changePageSize }}
        columns={[
          { header: 'رقم الأمر', render: (o) => <Typography fontWeight={700} color="primary">{o.orderNo}</Typography>, nowrap: true },
          { header: 'المصدر', render: (o) => SOURCES.find((s) => s.value === o.sourceType)?.label ?? o.sourceType },
          { header: 'المستودع', render: (o) => names.label(o.warehouseId) },
          { header: 'الحالة', render: (o) => <StatusChip status={o.status} /> },
          {
            header: 'إجراءات', nowrap: true,
            render: (o) => (
              <Stack direction="row" gap={1} alignItems="center">
                <DocumentViewButton load={() => issueOrderDocument(o.id, names.label)} />
                <Button size="small" variant="outlined" onClick={() => toggle(o.id)}>التفاصيل والسحب</Button>
              </Stack>
            ),
          },
        ]}
      />

      <Dialog open={Boolean(openId)} onClose={() => setOpenId('')} fullWidth maxWidth="md">
        <DialogTitle>أمر الصرف {detail?.order.orderNo ?? ''} {detail ? <StatusChip status={detail.order.status} /> : null}</DialogTitle>
        <DialogContent dividers>
          {actionError ? <Alert severity="error" sx={{ mb: 2 }}>{actionError}</Alert> : null}
          {detailLoading || !detail ? <LinearProgress /> : (
            <Stack spacing={2}>
              <Table size="small">
                <TableHead><TableRow sx={{ '& th': { fontWeight: 700 } }}><TableCell>الصنف</TableCell><TableCell align="left">مطلوب</TableCell><TableCell align="left">مسحوب</TableCell><TableCell align="left">مُتحقَّق</TableCell><TableCell align="left">مصروف</TableCell></TableRow></TableHead>
                <TableBody>
                  {detail.lines.map((l) => (
                    <TableRow key={l.id}>
                      <TableCell><Typography component="span" sx={{ fontFamily: 'monospace', fontWeight: 700 }} color="primary">{l.itemCode}</Typography> {l.itemName}</TableCell>
                      <TableCell align="left">{formatQty(l.requestedQty)}</TableCell><TableCell align="left">{formatQty(l.pickedQty)}</TableCell>
                      <TableCell align="left">{formatQty(l.verifiedQty)}</TableCell><TableCell align="left">{formatQty(l.issuedQty)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>

              {detail.pickTasks.length > 0 ? (
                <Box>
                  <Typography variant="caption" color="text.secondary" fontWeight={700}>مهام السحب</Typography>
                  {detail.pickTasks.map((t) => (
                    <Paper key={t.id} variant="outlined" sx={{ p: 1, mt: 1, borderRadius: 2, display: 'flex', gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
                      <Typography sx={{ fontFamily: 'monospace', fontWeight: 700 }}>{t.itemCode}</Typography>
                      <Typography variant="body2">من <b>{t.locationCode}</b> · الكمية <b>{formatQty(t.qty)}</b></Typography>
                      <StatusChip status={t.status} />
                      {t.status === 'PENDING' ? <Button size="small" variant="contained" disabled={busy === detail.order.id} onClick={() => void step(detail.order.id, () => issueOrdersApi.completePick(detail.order.id, t.id), 'تم السحب', 'تعذر إتمام السحب')}>تم السحب</Button> : null}
                      {t.status === 'PICKED' ? <Button size="small" variant="contained" color="success" disabled={busy === detail.order.id} onClick={() => void step(detail.order.id, () => issueOrdersApi.verifyPick(detail.order.id, t.id), 'تم التحقق', 'تعذر التحقق')}>تحقّق</Button> : null}
                    </Paper>
                  ))}
                </Box>
              ) : null}
            </Stack>
          )}
        </DialogContent>
        <DialogActions>
          {detail && detail.order.status === 'DRAFT' ? (
            <Button variant="contained" disabled={busy === detail.order.id} onClick={() => void step(detail.order.id, () => issueOrdersApi.generatePickTasks(detail.order.id), 'تم توليد مهام السحب', 'تعذر توليد المهام')}>⚙ توليد مهام السحب</Button>
          ) : null}
          {detail && detail.order.status !== 'ISSUED' && detail.pickTasks.length > 0 && detail.pickTasks.every((t) => t.status === 'VERIFIED') ? (
            <Button variant="contained" color="success" disabled={busy === detail.order.id} onClick={() => void step(detail.order.id, () => issueOrdersApi.issue(detail.order.id), 'تم صرف الأمر من المخزون', 'تعذر صرف الأمر')}>✓ صرف من المخزون</Button>
          ) : null}
          <Button onClick={() => setOpenId('')}>إغلاق</Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}

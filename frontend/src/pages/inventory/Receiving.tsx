import { useCallback, useState } from 'react';
import {
  Alert, Box, Button, Card, CardContent, Dialog, DialogActions, DialogContent, DialogTitle, LinearProgress, MenuItem, Paper, Stack, Table, TableBody,
  TableCell, TableHead, TableRow, TextField, Typography,
} from '@mui/material';
import PageHeader from '../../components/ui/PageHeader';
import DataTable from '../../components/ui/DataTable';
import StatusChip from '../../components/ui/StatusChip';
import { formatQty } from '../../lib/format';
import { receivingApi, type ReceivingLine } from '../../api/endpoints/receiving';
import { partiesApi } from '../../api/endpoints/parties';
import { unwrapList, unwrapNode, unwrapPaged } from '../../api/apiData';
import { usePagedList } from '../../hooks/usePagedList';
import { useLocationNames } from '../../hooks/useLocationNames';
import { toast, extractApiError } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';
import EntityPicker, { type PickerOption } from '../../components/pickers/EntityPicker';
import DocumentViewButton from '../../components/ui/DocumentViewButton';
import { receivingDocument } from '../../lib/wmsDocuments';
import LocationSelect from '../../components/pickers/LocationSelect';
import WmsLinesEditor, { type WmsLine } from '../../components/wms/WmsLinesEditor';

type ReceivingDoc = { id: string; documentNo: string; vendorPartyId?: string; purchaseOrderRef?: string; warehouseId: string; status: string; postedAt?: string };
type DocLine = { id: string; itemCode: string; itemName: string; expectedQty?: number; receivedQty: number; rejectedQty: number; assignedLocationId?: string; conditionStatus: string };
type DocDetail = ReceivingDoc & { notes?: string; lines: DocLine[] };
type PutawayTask = { id: string; qty: number; status: string; toLocationId: string };
type PartyRow = { id: string; displayNameAr?: string; displayName?: string };

const CONDITIONS = [
  { value: 'GOOD', label: 'سليم' },
  { value: 'DAMAGED', label: 'تالف' },
  { value: 'PARTIAL', label: 'جزئي' },
];

const conditionLabel = (c: string): string => CONDITIONS.find((x) => x.value === c)?.label ?? c;

function toApiLines(lines: WmsLine[]): ReceivingLine[] {
  return lines.filter((l) => Number(l.qty) > 0).map((l) => ({
    itemId: l.itemId,
    receivedQty: Number(l.qty),
    rejectedQty: Number(l.extra.rejected ?? 0),
    expectedQty: l.extra.expected === '' || l.extra.expected === undefined ? undefined : Number(l.extra.expected),
    assignedLocationId: l.locationId || undefined,
    conditionStatus: String(l.extra.condition ?? 'GOOD'),
  }));
}

function ReceivingLinesEditor({ lines, onChange, warehouseId }: { lines: WmsLine[]; onChange: (l: WmsLine[]) => void; warehouseId: string }): JSX.Element {
  return (
    <WmsLinesEditor
      lines={lines}
      onChange={onChange}
      warehouseId={warehouseId}
      locationLabel="موقع التخزين"
      qtyLabel="الكمية المستلمة"
      showAvailable={false}
      defaultExtra={() => ({ expected: '', rejected: 0, condition: 'GOOD' })}
      pickerTitle="اختيار الأصناف المستلمة"
      extraColumns={[
        { key: 'expected', label: 'المتوقعة', width: 110, render: (l, patch) => <TextField size="small" type="number" inputProps={{ min: 0 }} value={l.extra.expected ?? ''} onChange={(e) => patch({ expected: e.target.value })} /> },
        { key: 'rejected', label: 'المرفوضة', width: 110, render: (l, patch) => <TextField size="small" type="number" inputProps={{ min: 0 }} value={Number(l.extra.rejected ?? 0)} onChange={(e) => patch({ rejected: Number(e.target.value) })} /> },
        {
          key: 'condition', label: 'الحالة', width: 130,
          render: (l, patch) => (
            <TextField select size="small" fullWidth value={String(l.extra.condition ?? 'GOOD')} onChange={(e) => patch({ condition: e.target.value })}>
              {CONDITIONS.map((c) => <MenuItem key={c.value} value={c.value}>{c.label}</MenuItem>)}
            </TextField>
          ),
        },
      ]}
    />
  );
}

export default function Receiving(): JSX.Element {
  const names = useLocationNames();
  const list = usePagedList<ReceivingDoc>({
    errorMessage: 'تعذر تحميل مستندات الاستلام',
    fetcher: ({ page, pageSize }) => receivingApi.list(page, pageSize),
  });

  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState('');
  const [formError, setFormError] = useState('');
  const [warehouseId, setWarehouseId] = useState('');
  const [vendor, setVendor] = useState<PickerOption | null>(null);
  const [poRef, setPoRef] = useState('');
  const [notes, setNotes] = useState('');
  const [lines, setLines] = useState<WmsLine[]>([]);

  const [openId, setOpenId] = useState('');
  const [detail, setDetail] = useState<DocDetail | null>(null);
  const [tasks, setTasks] = useState<PutawayTask[]>([]);
  const [taskTargets, setTaskTargets] = useState<Record<string, string>>({});
  const [detailLoading, setDetailLoading] = useState(false);
  const [actionError, setActionError] = useState('');
  const [extraLines, setExtraLines] = useState<WmsLine[]>([]);

  const searchVendors = useCallback(async (text: string): Promise<PickerOption[]> => {
    const res = await partiesApi.getParties({ page: 1, pageSize: 10, typeCode: 'VENDOR', isActive: true, searchTerm: text || undefined });
    return unwrapPaged<PartyRow>(res.data).items.map((p) => ({ id: p.id, label: p.displayNameAr || p.displayName || p.id.slice(0, 8), sublabel: p.displayName }));
  }, []);

  const loadDetail = useCallback(async (id: string): Promise<void> => {
    setDetailLoading(true); setActionError('');
    try {
      const [d, t] = await Promise.all([receivingApi.get(id), receivingApi.getPutawayTasks(id)]);
      const doc = unwrapNode<DocDetail>(d.data);
      const rows = unwrapList<PutawayTask>(t.data);
      setDetail(doc); setTasks(rows);
      setTaskTargets(Object.fromEntries(rows.map((x) => [x.id, x.toLocationId])));
    } catch (e: unknown) { setActionError(extractApiError(e, 'تعذر تحميل تفاصيل المستند')); }
    finally { setDetailLoading(false); }
  }, []);

  function toggle(id: string): void {
    if (openId === id) { setOpenId(''); return; }
    setOpenId(id); setExtraLines([]);
    void loadDetail(id);
  }

  async function create(): Promise<void> {
    if (!warehouseId) { setFormError('اختر المستودع'); return; }
    setBusy('create'); setFormError('');
    try {
      const res = await receivingApi.create({ warehouseId, vendorPartyId: vendor?.id, purchaseOrderRef: poRef.trim() || undefined, notes: notes.trim() || undefined });
      const created = unwrapNode<{ id: string }>(res.data);
      const apiLines = toApiLines(lines);
      if (created?.id) for (const l of apiLines) await receivingApi.addLine(created.id, l);
      toast.success('تم إنشاء مستند الاستلام');
      setWarehouseId(''); setVendor(null); setPoRef(''); setNotes(''); setLines([]); setShowForm(false); list.reload();
    } catch (e: unknown) { setFormError(extractApiError(e, 'تعذر إنشاء المستند')); }
    finally { setBusy(''); }
  }

  async function addLines(id: string): Promise<void> {
    const apiLines = toApiLines(extraLines);
    if (apiLines.length === 0) { setActionError('أضف صنفاً واحداً على الأقل بكمية مستلمة'); return; }
    setBusy(id); setActionError('');
    try {
      for (const l of apiLines) await receivingApi.addLine(id, l);
      toast.success('تمت إضافة الأسطر');
      setExtraLines([]); await loadDetail(id);
    } catch (e: unknown) { setActionError(extractApiError(e, 'تعذر إضافة الأسطر')); }
    finally { setBusy(''); }
  }

  async function post(id: string): Promise<void> {
    setBusy(id); setActionError('');
    try { const res = await receivingApi.post(id); notifyResult(res, 'تم ترحيل مستند الاستلام'); list.reload(); await loadDetail(id); }
    catch (e: unknown) { setActionError(extractApiError(e, 'تعذر ترحيل المستند')); }
    finally { setBusy(''); }
  }

  async function completeTask(docId: string, t: PutawayTask): Promise<void> {
    const to = taskTargets[t.id];
    if (!to) { setActionError('اختر موقع التخزين'); return; }
    setBusy(t.id); setActionError('');
    try { await receivingApi.completePutaway(t.id, { toLocationId: to, qty: t.qty }); toast.success('تم التخزين'); await loadDetail(docId); }
    catch (e: unknown) { setActionError(extractApiError(e, 'تعذر إتمام مهمة التخزين')); }
    finally { setBusy(''); }
  }

  return (
    <Box>
      <PageHeader
        title="الاستلام والتخزين" subtitle="استلام البضاعة من الموردين وتخزينها"
        actions={<Button variant={showForm ? 'outlined' : 'contained'} onClick={() => setShowForm((s) => !s)}>{showForm ? '✕ إلغاء' : '＋ مستند استلام'}</Button>}
      />
      {list.error ? <Alert severity="error" sx={{ mb: 2 }}>{list.error}</Alert> : null}

      {showForm ? (
        <Card variant="outlined" sx={{ borderRadius: 3, mb: 3 }}>
          <CardContent>
            <Typography variant="h6" fontWeight={700} sx={{ mb: 2 }}>مستند استلام جديد</Typography>
            {formError ? <Alert severity="error" sx={{ mb: 2 }}>{formError}</Alert> : null}
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: 2, mb: 2 }}>
              <LocationSelect type="WAREHOUSE" label="المستودع *" value={warehouseId} onChange={(id) => setWarehouseId(id)} />
              <EntityPicker label="المورّد" value={vendor} onChange={setVendor} search={searchVendors} placeholder="ابحث باسم المورّد..." />
              <TextField size="small" label="مرجع أمر الشراء" value={poRef} onChange={(e) => setPoRef(e.target.value)} />
              <TextField size="small" label="ملاحظات" value={notes} onChange={(e) => setNotes(e.target.value)} />
            </Box>
            <ReceivingLinesEditor lines={lines} onChange={setLines} warehouseId={warehouseId} />
            <Button variant="contained" sx={{ mt: 2 }} disabled={busy === 'create'} onClick={() => void create()}>💾 حفظ المستند</Button>
          </CardContent>
        </Card>
      ) : null}

      <DataTable
        rows={list.items} getKey={(d) => d.id} loading={list.loading} empty="لا توجد مستندات"
        paging={{ page: list.page - 1, pageSize: list.pageSize, total: list.totalCount, onPage: (p) => list.setPage(p + 1), onPageSize: list.changePageSize }}
        columns={[
          { header: 'رقم المستند', render: (d) => <Typography fontWeight={700} color="primary">{d.documentNo}</Typography>, nowrap: true },
          { header: 'المستودع', render: (d) => names.label(d.warehouseId) },
          { header: 'مرجع الشراء', render: (d) => d.purchaseOrderRef || '—' },
          { header: 'الحالة', render: (d) => <StatusChip status={d.status} /> },
          { header: 'تاريخ الترحيل', render: (d) => (d.postedAt ? new Date(d.postedAt).toLocaleDateString('en-CA') : '—'), nowrap: true },
          {
            header: 'إجراءات', nowrap: true,
            render: (d) => (
              <Stack direction="row" gap={1} alignItems="center">
                <DocumentViewButton browse={{ kind: 'receiving', id: d.id, load: (id) => receivingDocument(id, names.label) }} />
                <Button size="small" variant="outlined" onClick={() => toggle(d.id)}>الأسطر والتخزين</Button>
              </Stack>
            ),
          },
        ]}
      />

      <Dialog open={Boolean(openId)} onClose={() => setOpenId('')} fullWidth maxWidth="lg">
        <DialogTitle>مستند الاستلام {detail?.documentNo ?? ''} {detail ? <StatusChip status={detail.status} /> : null}</DialogTitle>
        <DialogContent dividers>
          {actionError ? <Alert severity="error" sx={{ mb: 2 }}>{actionError}</Alert> : null}
          {detailLoading || !detail ? <LinearProgress /> : (
            <Stack spacing={2}>
              <Table size="small">
                <TableHead>
                  <TableRow sx={{ '& th': { fontWeight: 700 } }}>
                    <TableCell>الصنف</TableCell><TableCell align="left">المتوقعة</TableCell><TableCell align="left">المستلمة</TableCell><TableCell align="left">المرفوضة</TableCell><TableCell>الحالة</TableCell><TableCell>موقع التخزين</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {detail.lines.length === 0 ? (
                    <TableRow><TableCell colSpan={6} align="center" sx={{ color: 'text.secondary', py: 2 }}>لا توجد أسطر بعد</TableCell></TableRow>
                  ) : detail.lines.map((l) => (
                    <TableRow key={l.id}>
                      <TableCell><Typography component="span" sx={{ fontFamily: 'monospace', fontWeight: 700 }} color="primary">{l.itemCode}</Typography> {l.itemName}</TableCell>
                      <TableCell align="left">{l.expectedQty !== undefined && l.expectedQty !== null ? formatQty(l.expectedQty) : '—'}</TableCell>
                      <TableCell align="left">{formatQty(l.receivedQty)}</TableCell><TableCell align="left">{formatQty(l.rejectedQty)}</TableCell>
                      <TableCell>{conditionLabel(l.conditionStatus)}</TableCell>
                      <TableCell>{l.assignedLocationId ? names.label(l.assignedLocationId) : '—'}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>

              {detail.status !== 'POSTED' && detail.status !== 'COMPLETED' ? (
                <Box>
                  <Typography variant="caption" color="text.secondary" fontWeight={700}>إضافة أسطر</Typography>
                  <ReceivingLinesEditor lines={extraLines} onChange={setExtraLines} warehouseId={detail.warehouseId} />
                  <Stack direction="row" gap={1} sx={{ mt: 1 }}>
                    <Button variant="outlined" disabled={busy === detail.id} onClick={() => void addLines(detail.id)}>＋ إضافة للمستند</Button>
                    <Button variant="contained" color="success" disabled={busy === detail.id || detail.lines.length === 0} onClick={() => void post(detail.id)}>✓ ترحيل المستند</Button>
                  </Stack>
                </Box>
              ) : null}

              {tasks.length > 0 ? (
                <Box>
                  <Typography variant="caption" color="text.secondary" fontWeight={700}>مهام التخزين</Typography>
                  {tasks.map((t) => (
                    <Paper key={t.id} variant="outlined" sx={{ p: 1, mt: 1, borderRadius: 2, display: 'flex', gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
                      <Typography variant="body2">الكمية: <b>{formatQty(t.qty)}</b></Typography>
                      <StatusChip status={t.status} />
                      {t.status !== 'COMPLETED' ? (
                        <>
                          <Box sx={{ minWidth: 220 }}><LocationSelect value={taskTargets[t.id] ?? ''} allowEmpty={false} onChange={(id) => setTaskTargets({ ...taskTargets, [t.id]: id })} /></Box>
                          <Button size="small" variant="contained" disabled={busy === t.id} onClick={() => void completeTask(detail.id, t)}>إتمام التخزين</Button>
                        </>
                      ) : null}
                    </Paper>
                  ))}
                </Box>
              ) : null}
            </Stack>
          )}
        </DialogContent>
        <DialogActions><Button onClick={() => setOpenId('')}>إغلاق</Button></DialogActions>
      </Dialog>
    </Box>
  );
}

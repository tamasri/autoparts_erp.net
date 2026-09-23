import { useCallback, useState } from 'react';
import {
  Alert, Box, Button, Card, CardContent, Dialog, DialogActions, DialogContent, DialogTitle, LinearProgress, MenuItem, Stack, Table, TableBody,
  TableCell, TableHead, TableRow, TextField, Typography,
} from '@mui/material';
import PageHeader from '../../components/ui/PageHeader';
import DataTable from '../../components/ui/DataTable';
import StatusChip from '../../components/ui/StatusChip';
import { formatQty } from '../../lib/format';
import { cycleCountsApi } from '../../api/endpoints/cycleCounts';
import { unwrapNode } from '../../api/apiData';
import { usePagedList } from '../../hooks/usePagedList';
import { useLocationNames } from '../../hooks/useLocationNames';
import { toast, extractApiError } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';
import DocumentViewButton from '../../components/ui/DocumentViewButton';
import { cycleCountDocument } from '../../lib/wmsDocuments';
import LocationSelect from '../../components/pickers/LocationSelect';

type Plan = { id: string; warehouseId: string; scopeType: string; status: string; scheduledFor?: string };
type CountLine = { id: string; itemCode: string; itemName: string; locationCode: string; systemQty: number; countedQty: number | null; varianceQty: number };
type PlanDetail = Plan & { lines: CountLine[] };

const SCOPES = [
  { value: 'FULL', label: 'كامل' },
  { value: 'LOCATION', label: 'حسب الموقع' },
  { value: 'CATEGORY', label: 'حسب الفئة' },
  { value: 'ABC', label: 'تحليل ABC' },
];

const toLocalDate = (d: Date): string => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

export default function CycleCounts(): JSX.Element {
  const names = useLocationNames();
  const list = usePagedList<Plan>({
    errorMessage: 'تعذر تحميل خطط الجرد',
    fetcher: ({ page, pageSize }) => cycleCountsApi.list(page, pageSize),
  });

  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState('');
  const [formError, setFormError] = useState('');
  const [warehouseId, setWarehouseId] = useState('');
  const [scopeType, setScopeType] = useState('FULL');
  const [scheduledFor, setScheduledFor] = useState(toLocalDate(new Date()));

  const [openId, setOpenId] = useState('');
  const [detail, setDetail] = useState<PlanDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [counts, setCounts] = useState<Record<string, string>>({});
  const [actionError, setActionError] = useState('');

  const loadDetail = useCallback(async (id: string): Promise<void> => {
    setDetailLoading(true); setActionError('');
    try {
      const res = await cycleCountsApi.get(id);
      const d = unwrapNode<PlanDetail>(res.data);
      setDetail(d);
      setCounts(Object.fromEntries((d?.lines ?? []).map((l) => [l.id, l.countedQty === null ? '' : String(l.countedQty)])));
    } catch (e: unknown) { setActionError(extractApiError(e, 'تعذر تحميل أسطر الجرد')); }
    finally { setDetailLoading(false); }
  }, []);

  function togglePlan(id: string): void {
    if (openId === id) { setOpenId(''); return; }
    setOpenId(id);
    void loadDetail(id);
  }

  async function create(): Promise<void> {
    if (!warehouseId) { setFormError('اختر المستودع'); return; }
    setBusy('create'); setFormError('');
    try {
      await cycleCountsApi.create({ warehouseId, scopeType, scheduledFor });
      toast.success('تم إنشاء خطة الجرد وتوليد أسطرها من الأرصدة الحالية');
      setWarehouseId(''); setShowForm(false); list.reload();
    } catch (e: unknown) { setFormError(extractApiError(e, 'تعذر إنشاء خطة الجرد')); }
    finally { setBusy(''); }
  }

  async function submitCounts(): Promise<void> {
    if (!detail) return;
    const entered = detail.lines.filter((l) => counts[l.id] !== '' && counts[l.id] !== undefined);
    if (entered.length === 0) { setActionError('أدخل الكمية المعدودة لسطر واحد على الأقل'); return; }
    setBusy(detail.id); setActionError('');
    try {
      await cycleCountsApi.record(detail.id, entered.map((l) => ({ lineId: l.id, countedQty: Number(counts[l.id]) })));
      toast.success('تم تسجيل نتائج الجرد');
      list.reload(); await loadDetail(detail.id);
    } catch (e: unknown) { setActionError(extractApiError(e, 'تعذر تسجيل نتائج الجرد')); }
    finally { setBusy(''); }
  }

  async function approve(id: string): Promise<void> {
    setBusy(id);
    try { const res = await cycleCountsApi.approveVariance(id); notifyResult(res, 'تم اعتماد الفروقات وترحيلها'); list.reload(); setOpenId(''); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر اعتماد الفروقات')); }
    finally { setBusy(''); }
  }

  return (
    <Box>
      <PageHeader
        title="الجرد الدوري" subtitle="خطة الجرد تُولَّد أسطرها من الأرصدة الحالية؛ أدخل الكميات المعدودة ثم اعتمد الفروقات"
        actions={<Button variant={showForm ? 'outlined' : 'contained'} onClick={() => setShowForm((s) => !s)}>{showForm ? '✕ إلغاء' : '＋ خطة جرد'}</Button>}
      />
      {list.error ? <Alert severity="error" sx={{ mb: 2 }}>{list.error}</Alert> : null}

      {showForm ? (
        <Card variant="outlined" sx={{ borderRadius: 3, mb: 3 }}>
          <CardContent>
            <Typography variant="h6" fontWeight={700} sx={{ mb: 2 }}>خطة جرد جديدة</Typography>
            {formError ? <Alert severity="error" sx={{ mb: 2 }}>{formError}</Alert> : null}
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 2, mb: 2 }}>
              <LocationSelect type="WAREHOUSE" label="المستودع *" value={warehouseId} onChange={(id) => setWarehouseId(id)} />
              <TextField select size="small" label="نطاق الجرد" value={scopeType} onChange={(e) => setScopeType(e.target.value)}>
                {SCOPES.map((s) => <MenuItem key={s.value} value={s.value}>{s.label}</MenuItem>)}
              </TextField>
              <TextField size="small" type="date" label="تاريخ التنفيذ" InputLabelProps={{ shrink: true }} value={scheduledFor} onChange={(e) => setScheduledFor(e.target.value)} />
            </Box>
            <Button variant="contained" disabled={busy === 'create'} onClick={() => void create()}>💾 حفظ الخطة</Button>
          </CardContent>
        </Card>
      ) : null}

      <DataTable
        rows={list.items} getKey={(p) => p.id} loading={list.loading} empty="لا توجد خطط جرد"
        paging={{ page: list.page - 1, pageSize: list.pageSize, total: list.totalCount, onPage: (p) => list.setPage(p + 1), onPageSize: list.changePageSize }}
        columns={[
          { header: 'المستودع', render: (p) => <Typography fontWeight={600}>{names.label(p.warehouseId)}</Typography> },
          { header: 'النطاق', render: (p) => SCOPES.find((s) => s.value === p.scopeType)?.label ?? p.scopeType },
          { header: 'التاريخ', render: (p) => p.scheduledFor ?? '—', nowrap: true },
          { header: 'الحالة', render: (p) => <StatusChip status={p.status} /> },
          {
            header: 'إجراءات', nowrap: true,
            render: (p) => (
              <Stack direction="row" gap={1} alignItems="center">
                <DocumentViewButton load={() => cycleCountDocument(p.id, names.label)} />
                <Button size="small" variant="outlined" onClick={() => togglePlan(p.id)}>📋 أسطر الجرد</Button>
                {p.status === 'PENDING_APPROVAL' ? <Button size="small" variant="contained" color="success" disabled={busy === p.id} onClick={() => void approve(p.id)}>✓ اعتماد الفروقات</Button> : null}
              </Stack>
            ),
          },
        ]}
      />

      <Dialog open={Boolean(openId)} onClose={() => setOpenId('')} fullWidth maxWidth="md">
        <DialogTitle>أسطر الجرد {detail ? <>— {names.label(detail.warehouseId)} <StatusChip status={detail.status} /></> : null}</DialogTitle>
        <DialogContent dividers>
          {actionError ? <Alert severity="error" sx={{ mb: 2 }}>{actionError}</Alert> : null}
          {detailLoading || !detail ? <LinearProgress /> : (
            <Table size="small">
              <TableHead>
                <TableRow sx={{ '& th': { fontWeight: 700 } }}>
                  <TableCell>الصنف</TableCell><TableCell>الموقع</TableCell><TableCell align="left">رصيد النظام</TableCell><TableCell sx={{ width: 140 }}>الكمية المعدودة</TableCell><TableCell align="left">الفرق</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {detail.lines.length === 0 ? (
                  <TableRow><TableCell colSpan={5} align="center" sx={{ color: 'text.secondary', py: 3 }}>لا توجد أرصدة في هذا المستودع لتُجرد</TableCell></TableRow>
                ) : detail.lines.map((l) => {
                  const raw = counts[l.id];
                  const variance = raw === '' || raw === undefined ? null : Number(raw) - l.systemQty;
                  return (
                    <TableRow key={l.id}>
                      <TableCell><Typography component="span" sx={{ fontFamily: 'monospace', fontWeight: 700 }} color="primary">{l.itemCode}</Typography> <Typography component="span" variant="body2" color="text.secondary">{l.itemName}</Typography></TableCell>
                      <TableCell>{l.locationCode}</TableCell>
                      <TableCell align="left">{formatQty(l.systemQty)}</TableCell>
                      <TableCell><TextField size="small" type="number" inputProps={{ min: 0 }} value={raw ?? ''} disabled={detail.status === 'POSTED'} onChange={(e) => setCounts({ ...counts, [l.id]: e.target.value })} /></TableCell>
                      <TableCell align="left">
                        <Typography fontWeight={700} color={variance === null || variance === 0 ? 'text.secondary' : variance > 0 ? 'success.main' : 'error.main'}>
                          {variance === null ? '—' : (variance > 0 ? '+' : '') + formatQty(variance)}
                        </Typography>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          )}
        </DialogContent>
        <DialogActions>
          {detail && detail.status !== 'POSTED' && detail.lines.length > 0 ? (
            <Button variant="contained" disabled={busy === detail.id} onClick={() => void submitCounts()}>📤 حفظ نتائج الجرد</Button>
          ) : null}
          <Button onClick={() => setOpenId('')}>إغلاق</Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}

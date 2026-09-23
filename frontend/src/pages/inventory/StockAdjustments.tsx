import { useState } from 'react';
import { Alert, Box, Button, Card, CardContent, Chip, MenuItem, Stack, TextField, Typography } from '@mui/material';
import PageHeader from '../../components/ui/PageHeader';
import DataTable from '../../components/ui/DataTable';
import StatusChip from '../../components/ui/StatusChip';
import { formatQty } from '../../lib/format';
import { stockAdjustmentsApi } from '../../api/endpoints/stockAdjustments';
import { usePagedList } from '../../hooks/usePagedList';
import { useLocationNames } from '../../hooks/useLocationNames';
import { toast, extractApiError } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';
import LocationSelect from '../../components/pickers/LocationSelect';
import ReasonCodeSelect from '../../components/pickers/ReasonCodeSelect';
import DocumentViewButton from '../../components/ui/DocumentViewButton';
import { adjustmentDocument } from '../../lib/wmsDocuments';
import WmsLinesEditor, { type WmsLine } from '../../components/wms/WmsLinesEditor';

type StockAdjustment = {
  id: string;
  adjustmentNo: string;
  adjustmentType: string;
  warehouseId: string;
  reasonCode: string;
  status: string;
  postedAt?: string;
};

// Values allowed by the database (stock_adjustments_adjustment_type_check).
const TYPES: { value: string; label: string; color: 'warning' | 'error' | 'success' | 'default' }[] = [
  { value: 'MANUAL', label: 'تسوية يدوية', color: 'warning' },
  { value: 'DAMAGE', label: 'تالف', color: 'error' },
  { value: 'FOUND', label: 'زيادة (عُثر عليها)', color: 'success' },
  { value: 'CYCLE_COUNT', label: 'نتيجة جرد', color: 'default' },
];

const systemBefore = (line: WmsLine, locationId: string): number => line.item.stock.find((s) => s.locationId === locationId)?.available ?? 0;

export default function StockAdjustments(): JSX.Element {
  const names = useLocationNames();
  const list = usePagedList<StockAdjustment>({
    errorMessage: 'تعذر تحميل تسويات المخزون',
    fetcher: ({ page, pageSize }) => stockAdjustmentsApi.list(page, pageSize),
  });

  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState('');
  const [formError, setFormError] = useState('');
  const [adjustmentType, setAdjustmentType] = useState('MANUAL');
  const [warehouseId, setWarehouseId] = useState('');
  const [reasonCode, setReasonCode] = useState('');
  const [lines, setLines] = useState<WmsLine[]>([]);

  // "extra.delta" is the signed change; the quantity before is read from the item's real stock at that location.
  const deltaOf = (l: WmsLine): number => Number(l.extra.delta ?? 0);

  async function create(): Promise<void> {
    if (!warehouseId) { setFormError('اختر المستودع'); return; }
    if (!reasonCode) { setFormError('اختر سبب التسوية'); return; }
    const valid = lines.filter((l) => deltaOf(l) !== 0);
    if (valid.length === 0) { setFormError('أضف صنفاً واحداً على الأقل بمقدار تغيير غير صفري'); return; }
    const negative = valid.find((l) => systemBefore(l, l.locationId) + deltaOf(l) < 0);
    if (negative) { setFormError(`النتيجة سالبة للصنف ${negative.code} — لا يمكن أن ينزل الرصيد عن الصفر`); return; }
    setBusy('create'); setFormError('');
    try {
      await stockAdjustmentsApi.create({
        adjustmentType,
        warehouseId,
        reasonCode,
        lines: valid.map((l) => {
          const before = systemBefore(l, l.locationId);
          return { itemId: l.itemId, locationId: l.locationId, status: 'AVAILABLE', qtyDelta: deltaOf(l), systemQtyBefore: before, systemQtyAfter: before + deltaOf(l) };
        }),
      });
      toast.success('تم إنشاء التسوية (مسودة)');
      setWarehouseId(''); setReasonCode(''); setLines([]); setShowForm(false); list.reload();
    } catch (e: unknown) { setFormError(extractApiError(e, 'تعذر إنشاء التسوية')); }
    finally { setBusy(''); }
  }

  async function post(id: string): Promise<void> {
    setBusy(id);
    try { const res = await stockAdjustmentsApi.post(id); notifyResult(res, 'تم ترحيل التسوية'); list.reload(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر ترحيل التسوية')); }
    finally { setBusy(''); }
  }

  return (
    <Box>
      <PageHeader
        title="تسويات المخزون" subtitle="تعديل أرصدة المخزون وتسوية الفروقات"
        actions={<Button variant={showForm ? 'outlined' : 'contained'} onClick={() => setShowForm((s) => !s)}>{showForm ? '✕ إلغاء' : '＋ تسوية جديدة'}</Button>}
      />
      {list.error ? <Alert severity="error" sx={{ mb: 2 }}>{list.error}</Alert> : null}

      {showForm ? (
        <Card variant="outlined" sx={{ borderRadius: 3, mb: 3 }}>
          <CardContent>
            <Typography variant="h6" fontWeight={700} sx={{ mb: 2 }}>تسوية مخزون جديدة</Typography>
            {formError ? <Alert severity="error" sx={{ mb: 2 }}>{formError}</Alert> : null}
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 2, mb: 2 }}>
              <TextField select size="small" label="نوع التسوية" value={adjustmentType} onChange={(e) => setAdjustmentType(e.target.value)}>
                {TYPES.map((t) => <MenuItem key={t.value} value={t.value}>{t.label}</MenuItem>)}
              </TextField>
              <LocationSelect type="WAREHOUSE" label="المستودع *" value={warehouseId} onChange={(id) => setWarehouseId(id)} />
              <ReasonCodeSelect category="STOCK_ADJUST" label="سبب التسوية *" value={reasonCode} onChange={setReasonCode} />
            </Box>

            <WmsLinesEditor
              lines={lines}
              onChange={setLines}
              warehouseId={warehouseId}
              qtyLabel="الرصيد الحالي"
              showAvailable={false}
              defaultExtra={() => ({ delta: 0 })}
              pickerTitle="اختيار أصناف التسوية"
              extraColumns={[
                {
                  key: 'delta', label: 'مقدار التغيير (+/−)', width: 140,
                  render: (l, patch) => <TextField size="small" type="number" value={Number(l.extra.delta ?? 0)} onChange={(e) => patch({ delta: Number(e.target.value) })} />,
                },
                {
                  key: 'after', label: 'الناتج', width: 110,
                  render: (l) => {
                    const after = systemBefore(l, l.locationId) + deltaOf(l);
                    return <Typography fontWeight={700} color={after >= 0 ? 'success.main' : 'error.main'}>{formatQty(after)}</Typography>;
                  },
                },
              ]}
            />
            <Button variant="contained" sx={{ mt: 2 }} disabled={busy === 'create'} onClick={() => void create()}>💾 حفظ التسوية</Button>
          </CardContent>
        </Card>
      ) : null}

      <DataTable
        rows={list.items} getKey={(a) => a.id} loading={list.loading} empty="لا توجد تسويات"
        paging={{ page: list.page - 1, pageSize: list.pageSize, total: list.totalCount, onPage: (p) => list.setPage(p + 1), onPageSize: list.changePageSize }}
        columns={[
          { header: 'رقم التسوية', render: (a) => <Typography fontWeight={700} color="primary">{a.adjustmentNo}</Typography>, nowrap: true },
          { header: 'النوع', render: (a) => { const t = TYPES.find((x) => x.value === a.adjustmentType); return <Chip size="small" variant="outlined" color={t?.color ?? 'default'} label={t?.label ?? a.adjustmentType} />; } },
          { header: 'المستودع', render: (a) => names.label(a.warehouseId) },
          { header: 'السبب', render: (a) => <Typography variant="body2" color="text.secondary">{a.reasonCode}</Typography> },
          { header: 'الحالة', render: (a) => <StatusChip status={a.status} /> },
          {
            header: 'إجراءات', nowrap: true,
            render: (a) => (
              <Stack direction="row" gap={1} alignItems="center">
                <DocumentViewButton load={() => adjustmentDocument(a.id, names.label)} />
                {a.status !== 'POSTED' ? <Button size="small" variant="contained" color="success" disabled={busy === a.id} onClick={() => void post(a.id)}>✓ ترحيل</Button> : null}
              </Stack>
            ),
          },
        ]}
      />
    </Box>
  );
}

/** Transfer orders between warehouses: create (lines from the item picker), ship from the source, receive at the destination. */
import { useState } from 'react';
import { Alert, Box, Button, Card, CardContent, Chip, Stack, Typography } from '@mui/material';
import { transfersApi } from '../../api/endpoints/transfers';
import { usePagedList } from '../../hooks/usePagedList';
import { useLocationNames } from '../../hooks/useLocationNames';
import { toast, extractApiError } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';
import PageHeader from '../../components/ui/PageHeader';
import DataTable from '../../components/ui/DataTable';
import StatusChip from '../../components/ui/StatusChip';
import LocationSelect from '../../components/pickers/LocationSelect';
import DocumentViewButton from '../../components/ui/DocumentViewButton';
import { transferDocument } from '../../lib/wmsDocuments';
import WmsLinesEditor, { type WmsLine } from '../../components/wms/WmsLinesEditor';

type TransferOrder = {
  id: string;
  transferNo: string;
  sourceWarehouseId: string;
  destinationWarehouseId: string;
  status: string;
  shippedAt?: string;
  receivedAt?: string;
};

export default function Transfers(): JSX.Element {
  const names = useLocationNames();
  const list = usePagedList<TransferOrder>({
    errorMessage: 'تعذر تحميل أوامر التحويل',
    fetcher: ({ page, pageSize }) => transfersApi.listOrders(page, pageSize),
  });

  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState('');
  const [formError, setFormError] = useState('');
  const [sourceWarehouseId, setSourceWarehouseId] = useState('');
  const [destinationWarehouseId, setDestinationWarehouseId] = useState('');
  const [lines, setLines] = useState<WmsLine[]>([]);

  async function create(): Promise<void> {
    if (!sourceWarehouseId || !destinationWarehouseId) { setFormError('اختر مستودع المصدر والوجهة'); return; }
    if (sourceWarehouseId === destinationWarehouseId) { setFormError('مستودع المصدر والوجهة يجب أن يختلفا'); return; }
    const valid = lines.filter((l) => Number(l.qty) > 0);
    if (valid.length === 0) { setFormError('أضف صنفاً واحداً على الأقل بكمية صحيحة'); return; }
    const over = valid.find((l) => Number(l.qty) > (l.item.stock.find((s) => s.locationId === l.locationId)?.available ?? 0));
    if (over) { setFormError(`الكمية تتجاوز المتاح في الموقع المصدر للصنف ${over.code}`); return; }
    setBusy('create'); setFormError('');
    try {
      await transfersApi.createOrder({
        sourceWarehouseId,
        destinationWarehouseId,
        lines: valid.map((l) => ({
          itemId: l.itemId,
          sourceLocationId: l.locationId,
          destinationLocationId: String(l.extra.destination || destinationWarehouseId),
          shippedQty: Number(l.qty),
        })),
      });
      toast.success('تم إنشاء أمر التحويل');
      setSourceWarehouseId(''); setDestinationWarehouseId(''); setLines([]); setShowForm(false); list.reload();
    } catch (e: unknown) { setFormError(extractApiError(e, 'تعذر إنشاء أمر التحويل')); }
    finally { setBusy(''); }
  }

  async function act(id: string, kind: 'ship' | 'receive'): Promise<void> {
    setBusy(id);
    try {
      const res = kind === 'ship' ? await transfersApi.ship(id) : await transfersApi.receive(id);
      notifyResult(res, kind === 'ship' ? 'تم شحن أمر التحويل' : 'تم استلام أمر التحويل');
      list.reload();
    } catch (e: unknown) { toast.error(extractApiError(e, kind === 'ship' ? 'تعذر شحن أمر التحويل' : 'تعذر استلام أمر التحويل')); }
    finally { setBusy(''); }
  }

  return (
    <Box>
      <PageHeader
        title="التحويلات بين المستودعات" subtitle="نقل المخزون بين المستودعات والمواقع — الشحن من المصدر والاستلام في الوجهة"
        actions={<Button variant={showForm ? 'outlined' : 'contained'} onClick={() => setShowForm((s) => !s)}>{showForm ? '✕ إلغاء' : '＋ أمر تحويل'}</Button>}
      />
      {list.error ? <Alert severity="error" sx={{ mb: 2 }}>{list.error}</Alert> : null}

      {showForm ? (
        <Card variant="outlined" sx={{ borderRadius: 3, mb: 3 }}>
          <CardContent>
            <Typography variant="h6" fontWeight={700} sx={{ mb: 2 }}>أمر تحويل جديد</Typography>
            {formError ? <Alert severity="error" sx={{ mb: 2 }}>{formError}</Alert> : null}
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: 2, mb: 2 }}>
              <LocationSelect type="WAREHOUSE" label="مستودع المصدر *" value={sourceWarehouseId} onChange={(id) => setSourceWarehouseId(id)} />
              <LocationSelect type="WAREHOUSE" label="مستودع الوجهة *" value={destinationWarehouseId} onChange={(id) => setDestinationWarehouseId(id)} />
            </Box>
            <WmsLinesEditor
              lines={lines}
              onChange={setLines}
              warehouseId={sourceWarehouseId}
              locationLabel="من موقع"
              qtyLabel="الكمية المنقولة"
              showAvailable
              defaultExtra={() => ({ destination: destinationWarehouseId })}
              pickerTitle="اختيار الأصناف المراد نقلها"
              extraColumns={[{
                key: 'destination', label: 'إلى موقع', width: 200,
                render: (l, patch) => (
                  <LocationSelect value={String(l.extra.destination || destinationWarehouseId)} allowEmpty={false} onChange={(id) => patch({ destination: id })} />
                ),
              }]}
            />
            <Button variant="contained" sx={{ mt: 2 }} disabled={busy === 'create'} onClick={() => void create()}>💾 حفظ الأمر</Button>
          </CardContent>
        </Card>
      ) : null}

      <DataTable
        rows={list.items} getKey={(o) => o.id} loading={list.loading} empty="لا توجد أوامر تحويل"
        paging={{ page: list.page - 1, pageSize: list.pageSize, total: list.totalCount, onPage: (p) => list.setPage(p + 1), onPageSize: list.changePageSize }}
        columns={[
          { header: 'رقم الأمر', render: (o) => <Typography fontWeight={700} color="primary">{o.transferNo}</Typography>, nowrap: true },
          { header: 'المصدر', render: (o) => <Chip size="small" variant="outlined" label={names.label(o.sourceWarehouseId)} /> },
          { header: 'الوجهة', render: (o) => <Chip size="small" color="primary" variant="outlined" label={names.label(o.destinationWarehouseId)} /> },
          { header: 'الحالة', render: (o) => <StatusChip status={o.status} /> },
          {
            header: 'إجراءات',
            nowrap: true,
            render: (o) => (
              <Stack direction="row" gap={1} alignItems="center">
                <DocumentViewButton browse={{ kind: 'transfer-orders', id: o.id, load: (id) => transferDocument(id, names.label) }} />
                {o.status === 'DRAFT' ? <Button size="small" variant="contained" disabled={busy === o.id} onClick={() => void act(o.id, 'ship')}>✈ شحن</Button> : null}
                {o.status === 'IN_TRANSIT' || o.status === 'SHIPPED' ? <Button size="small" variant="contained" color="success" disabled={busy === o.id} onClick={() => void act(o.id, 'receive')}>✓ استلام</Button> : null}
              </Stack>
            ),
          },
        ]}
      />
    </Box>
  );
}

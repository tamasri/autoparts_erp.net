import { useState } from 'react';
import { transfersApi } from '../../api/endpoints/transfers';
import { usePagedList } from '../../hooks/usePagedList';
import { useLocationNames } from '../../hooks/useLocationNames';
import { toast, extractApiError } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';
import Pagination from '../../components/common/Pagination';
import ErrorBanner from '../../components/common/ErrorBanner';
import StatusBadge from '../../components/common/StatusBadge';
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
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">التحويلات بين المستودعات</h1>
          <div className="vex-page-header__breadcrumb">نقل المخزون بين المستودعات والمواقع</div>
        </div>
        <button type="button" onClick={() => setShowForm((s) => !s)} className={showForm ? 'btn-ghost' : 'btn-primary'}>
          {showForm ? '✕ إلغاء' : '＋ أمر تحويل'}
        </button>
      </div>

      {list.error ? <ErrorBanner message={list.error} /> : null}

      {showForm ? (
        <div className="vex-card" style={{ marginBottom: 20 }}>
          <h2 className="vex-section-title">أمر تحويل جديد</h2>
          {formError ? <ErrorBanner message={formError} /> : null}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: 16, marginBottom: 20 }}>
            <label className="vex-label">مستودع المصدر *<LocationSelect type="WAREHOUSE" value={sourceWarehouseId} onChange={(id) => setSourceWarehouseId(id)} /></label>
            <label className="vex-label">مستودع الوجهة *<LocationSelect type="WAREHOUSE" value={destinationWarehouseId} onChange={(id) => setDestinationWarehouseId(id)} /></label>
          </div>
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
          <div style={{ marginTop: 14 }}>
            <button type="button" disabled={busy === 'create'} onClick={() => void create()} className="btn-primary">💾 حفظ الأمر</button>
          </div>
        </div>
      ) : null}

      <div className="vex-card vex-card--no-pad" style={{ opacity: list.loading ? 0.6 : 1 }}>
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead><tr><th>رقم الأمر</th><th>المصدر</th><th>الوجهة</th><th>الحالة</th><th>إجراءات</th></tr></thead>
            <tbody>
              {list.items.length === 0 ? (
                <tr><td colSpan={5} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>لا توجد أوامر تحويل</td></tr>
              ) : list.items.map((o) => (
                <tr key={o.id}>
                  <td style={{ fontWeight: 600, color: 'var(--clr-primary)' }}>{o.transferNo}</td>
                  <td><span className="badge badge--draft">{names.label(o.sourceWarehouseId)}</span></td>
                  <td><span className="badge badge--primary">{names.label(o.destinationWarehouseId)}</span></td>
                  <td><StatusBadge status={o.status} type="invoice" /></td>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    <DocumentViewButton load={() => transferDocument(o.id, names.label)} />{' '}
                    {o.status !== 'SHIPPED' && o.status !== 'RECEIVED' ? (
                      <button type="button" disabled={busy === o.id} onClick={() => void act(o.id, 'ship')} className="btn-primary" style={{ padding: '5px 14px', fontSize: 12 }}>✈ شحن</button>
                    ) : null}
                    {o.status === 'SHIPPED' ? (
                      <button type="button" disabled={busy === o.id} onClick={() => void act(o.id, 'receive')} className="btn-success" style={{ padding: '5px 14px', fontSize: 12 }}>✓ استلام</button>
                    ) : null}
                    {o.status === 'RECEIVED' ? <span className="badge badge--success">مكتمل</span> : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <Pagination page={list.page} pageSize={list.pageSize} totalCount={list.totalCount} onPageChange={list.setPage} onPageSizeChange={list.changePageSize} />
      </div>
    </div>
  );
}

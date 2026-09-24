import { useCallback, useEffect, useState } from 'react';
import { Alert, Button, Chip, Stack } from '@mui/material';
import { inventoryAlertsApi } from '../../api/endpoints/inventoryAlerts';
import { unwrapList } from '../../api/apiData';
import { extractApiError, toast } from '../../lib/toast';
import PageHeader from '../../components/ui/PageHeader';
import DataTable, { type Column } from '../../components/ui/DataTable';
import ReasonDialog from '../../components/ui/ReasonDialog';
import StatusChip from '../../components/ui/StatusChip';
import { useRealtimeStore } from '../../stores/realtimeStore';

type StockAlert = {
  id: string; itemId: string; alertType: string; severity: string; message: string;
  thresholdValue?: number; currentValue?: number; status: string; createdAt: string;
};

const SEVERITY: Record<string, { label: string; color: 'error' | 'warning' | 'info' | 'success' }> = {
  CRITICAL: { label: 'حرج', color: 'error' }, HIGH: { label: 'عالٍ', color: 'warning' }, MEDIUM: { label: 'متوسط', color: 'info' }, LOW: { label: 'منخفض', color: 'success' },
};

export default function InventoryAlerts(): JSX.Element {
  const [rows, setRows] = useState<StockAlert[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState('');
  const [resolving, setResolving] = useState<StockAlert | null>(null);

  const load = useCallback(async () => {
    setLoading(true); setError('');
    try { setRows(unwrapList<StockAlert>((await inventoryAlertsApi.list()).data)); }
    catch (e: unknown) { setError(extractApiError(e, 'تعذر تحميل التنبيهات')); }
    finally { setLoading(false); }
  }, []);
  // New alerts pushed by the server appear without a manual refresh.
  const alertsVersion = useRealtimeStore((s) => s.alertsVersion);
  useEffect(() => { void load(); }, [load, alertsVersion]);

  async function acknowledge(id: string): Promise<void> {
    setBusy(id);
    try { await inventoryAlertsApi.acknowledge(id); toast.success('تم تأكيد التنبيه'); await load(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر تأكيد التنبيه')); }
    finally { setBusy(''); }
  }

  async function resolve(note: string): Promise<void> {
    if (!resolving) return;
    try { await inventoryAlertsApi.resolve(resolving.id, note || undefined); toast.success('تم إغلاق التنبيه'); setResolving(null); await load(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر إغلاق التنبيه')); }
  }

  const active = rows.filter((r) => r.status !== 'RESOLVED');
  const critical = active.filter((r) => r.severity === 'CRITICAL').length;

  const columns: Column<StockAlert>[] = [
    { header: 'الخطورة', render: (a) => <Chip size="small" color={SEVERITY[a.severity]?.color ?? 'default'} label={SEVERITY[a.severity]?.label ?? a.severity} /> },
    { header: 'النوع', render: (a) => <Chip size="small" variant="outlined" label={a.alertType} sx={{ fontFamily: 'monospace' }} /> },
    { header: 'الرسالة', render: (a) => a.message },
    { header: 'الحالي / الحد', numeric: true, render: (a) => (a.currentValue !== undefined ? `${a.currentValue} / ${a.thresholdValue ?? '—'}` : '—') },
    { header: 'التاريخ', nowrap: true, render: (a) => new Date(a.createdAt).toLocaleString('ar') },
    { header: 'الحالة', render: (a) => <StatusChip status={a.status} /> },
    {
      header: '', nowrap: true,
      render: (a) => a.status === 'RESOLVED' ? null : (
        <Stack direction="row" gap={1}>
          {a.status === 'OPEN' ? <Button size="small" variant="outlined" disabled={busy === a.id} onClick={() => void acknowledge(a.id)}>تأكيد الاطلاع</Button> : null}
          <Button size="small" variant="contained" color="success" disabled={busy === a.id} onClick={() => setResolving(a)}>إغلاق</Button>
        </Stack>
      ),
    },
  ];

  return (
    <>
      <PageHeader title="تنبيهات المخزون" subtitle="أصناف نافدة أو تحت حد إعادة الطلب"
        actions={<><Chip variant="outlined" label={`نشط: ${active.length}`} />{critical > 0 ? <Chip color="error" label={`حرج: ${critical}`} /> : null}</>} />
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      <DataTable columns={columns} rows={rows} getKey={(a) => a.id} loading={loading} empty="لا توجد تنبيهات" />
      <ReasonDialog open={resolving !== null} title="إغلاق التنبيه" confirmLabel="إغلاق" minLength={0} optionalNote onClose={() => setResolving(null)} onConfirm={resolve} />
    </>
  );
}

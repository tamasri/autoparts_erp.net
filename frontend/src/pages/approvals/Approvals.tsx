import { useEffect, useState } from 'react';
import { Alert, Button, Chip, Stack } from '@mui/material';
import { approvalsApi } from '../../api/endpoints/approvals';
import { usersApi } from '../../api/endpoints/users';
import { unwrapPaged } from '../../api/apiData';
import { usePagedList } from '../../hooks/usePagedList';
import { extractApiError, toast } from '../../lib/toast';
import PageHeader from '../../components/ui/PageHeader';
import DataTable, { type Column } from '../../components/ui/DataTable';
import ReasonDialog from '../../components/ui/ReasonDialog';
import StatusChip from '../../components/ui/StatusChip';

type Approval = {
  id: string; entityType?: string; actionCode?: string; reason?: string; status?: string; requestedByUserId?: string;
  requiredApprovals?: number; currentApprovals?: number; requestedAtUtc?: string; completedAtUtc?: string | null;
  /** Warehouses a held transfer touches (source first); approved by their managers. */
  warehouses?: string[];
};

/** What each governed action is, in words (the code name is shown for anything not listed). */
const ACTION: Record<string, string> = {
  ShipTransferOrderCommand: 'شحن أمر تحويل', TransferStockCommand: 'تحويل مخزون مباشر', CreateTransferRequestCommand: 'طلب تحويل',
  PostJournalEntryCommand: 'ترحيل قيد', VoidJournalEntryCommand: 'إلغاء قيد', PostInvoiceCommand: 'ترحيل فاتورة', VoidInvoiceCommand: 'إلغاء فاتورة',
  AssignRolesToUserCommand: 'تعديل أدوار مستخدم', DeactivateUserCommand: 'إيقاف مستخدم', SetUserWarehousesCommand: 'تعيين مستودعات مستخدم',
  LockPeriodCommand: 'إقفال فترة', UnlockPeriodCommand: 'فتح فترة',
};
const OPEN = new Set(['PENDING', 'IN_REVIEW']);

const when = (v?: string): string => (v ? new Date(v).toLocaleString('ar') : '—');

export default function Approvals(): JSX.Element {
  const list = usePagedList<Approval>({ errorMessage: 'تعذر تحميل الطلبات', fetcher: ({ page, pageSize }) => approvalsApi.getPending(page, pageSize) });
  const [busy, setBusy] = useState('');
  const [names, setNames] = useState<Record<string, string>>({});
  useEffect(() => {
    usersApi.getUsers(1, 100)
      .then((r) => setNames(Object.fromEntries(unwrapPaged<{ id: string; userName?: string }>(r.data).items.map((u) => [u.id, u.userName ?? '']))))
      .catch(() => undefined);
  }, []);
  const [rejecting, setRejecting] = useState<Approval | null>(null);

  async function approve(id: string): Promise<void> {
    setBusy(id);
    try { await approvalsApi.approve(id); toast.success('تمت الموافقة'); list.reload(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر الموافقة على الطلب')); }
    finally { setBusy(''); }
  }

  async function reject(reason: string): Promise<void> {
    if (!rejecting) return;
    try { await approvalsApi.reject(rejecting.id, reason); toast.success('تم رفض الطلب'); setRejecting(null); list.reload(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر رفض الطلب')); }
  }

  const columns: Column<Approval>[] = [
    { header: 'الإجراء', render: (r) => <Chip size="small" variant="outlined" label={ACTION[r.actionCode ?? ''] ?? (r.actionCode ?? '—').replace(/Command$/, '')} /> },
    { header: 'المستودعات', render: (r) => (r.warehouses?.length ? r.warehouses.join(' ← ') : '—') },
    { header: 'الكيان', render: (r) => r.entityType ?? '—' },
    { header: 'الطالب', render: (r) => names[r.requestedByUserId ?? ''] || '—' },
    { header: 'السبب', render: (r) => r.reason || '—' },
    { header: 'الموافقات', numeric: true, render: (r) => `${r.currentApprovals ?? 0} / ${r.requiredApprovals ?? 1}` },
    { header: 'تاريخ الطلب', render: (r) => when(r.requestedAtUtc), nowrap: true },
    { header: 'الحالة', render: (r) => <StatusChip status={r.status ?? 'PENDING'} /> },
    {
      header: '', nowrap: true,
      render: (r) => !OPEN.has(r.status ?? 'PENDING') ? null : (
        <Stack direction="row" gap={1}>
          <Button size="small" variant="contained" color="success" disabled={busy === r.id} onClick={() => void approve(r.id)}>✓ موافقة</Button>
          <Button size="small" variant="outlined" color="error" disabled={busy === r.id} onClick={() => setRejecting(r)}>✕ رفض</Button>
        </Stack>
      ),
    },
  ];

  return (
    <>
      <PageHeader title="طلبات الموافقة" subtitle="مراجعة والبت في الطلبات المعلّقة" actions={list.totalCount > 0 ? <Chip color="warning" label={`معلّق: ${list.totalCount}`} /> : undefined} />
      {list.error ? <Alert severity="error" sx={{ mb: 2 }}>{list.error}</Alert> : null}
      <DataTable
        columns={columns} rows={list.items} getKey={(r) => r.id} loading={list.loading} empty="لا توجد طلبات موافقة معلّقة"
        paging={{ page: list.page - 1, pageSize: list.pageSize, total: list.totalCount, onPage: (p) => list.setPage(p + 1), onPageSize: list.changePageSize }}
      />
      <ReasonDialog open={rejecting !== null} title="رفض الطلب" confirmLabel="تأكيد الرفض" onClose={() => setRejecting(null)} onConfirm={reject} />
    </>
  );
}

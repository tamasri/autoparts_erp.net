import { useState } from 'react';
import { Alert, Button, Chip, Paper, Stack, TextField } from '@mui/material';
import { auditApi } from '../../api/endpoints/audit';
import { usePagedList } from '../../hooks/usePagedList';
import PageHeader from '../../components/ui/PageHeader';
import DataTable, { type Column } from '../../components/ui/DataTable';

type AuditRow = { id: string; occurredAtUtc?: string; actorName?: string; action?: string; entityType?: string; entityId?: string; ipAddress?: string; details?: string | null };
type Filters = { module: string; entityType: string; from: string; to: string };

const EMPTY: Filters = { module: '', entityType: '', from: '', to: '' };

const COLUMNS: Column<AuditRow>[] = [
  { header: 'الوقت', render: (r) => (r.occurredAtUtc ? new Date(r.occurredAtUtc).toLocaleString('ar') : '—'), nowrap: true },
  { header: 'المستخدم', render: (r) => r.actorName ?? '—' },
  { header: 'الإجراء', render: (r) => <Chip size="small" color="primary" variant="outlined" label={r.action ?? '—'} sx={{ fontFamily: 'monospace' }} /> },
  { header: 'الكيان', render: (r) => r.entityType ?? '—' },
  { header: 'المعرّف', render: (r) => <span style={{ fontFamily: 'monospace', fontSize: 12 }}>{(r.entityId ?? '').slice(0, 8) || '—'}</span> },
  { header: 'العنوان', render: (r) => <span style={{ fontFamily: 'monospace', fontSize: 12 }}>{r.ipAddress ?? '—'}</span> },
];

export default function AuditLog(): JSX.Element {
  const [filters, setFilters] = useState<Filters>(EMPTY);
  const [applied, setApplied] = useState<Filters>(EMPTY);

  const list = usePagedList<AuditRow>({
    errorMessage: 'تعذر تحميل سجل التدقيق',
    pageSize: 50,
    deps: [applied],
    fetcher: ({ page, pageSize }) => {
      const params: Record<string, unknown> = { page, pageSize };
      if (applied.module.trim()) params.module = applied.module.trim();
      if (applied.entityType.trim()) params.entityType = applied.entityType.trim();
      if (applied.from) params.from = new Date(applied.from).toISOString();
      if (applied.to) params.to = new Date(applied.to).toISOString();
      return auditApi.getLogs(params);
    },
  });

  const set = (key: keyof Filters) => (e: React.ChangeEvent<HTMLInputElement>): void => setFilters({ ...filters, [key]: e.target.value });

  return (
    <>
      <PageHeader title="سجل التدقيق" subtitle="تتبع كل العمليات والتغييرات في النظام" />
      {list.error ? <Alert severity="error" sx={{ mb: 2 }}>{list.error}</Alert> : null}
      <Paper variant="outlined" sx={{ p: 2, mb: 2, borderRadius: 3 }}>
        <Stack direction="row" gap={2} flexWrap="wrap" alignItems="center">
          <TextField size="small" label="الوحدة" placeholder="INVOICES" value={filters.module} onChange={set('module')} />
          <TextField size="small" label="نوع الكيان" placeholder="Invoice" value={filters.entityType} onChange={set('entityType')} />
          <TextField size="small" type="date" label="من تاريخ" value={filters.from} onChange={set('from')} InputLabelProps={{ shrink: true }} />
          <TextField size="small" type="date" label="إلى تاريخ" value={filters.to} onChange={set('to')} InputLabelProps={{ shrink: true }} />
          <Button variant="contained" size="small" onClick={() => setApplied(filters)}>تطبيق</Button>
          <Button size="small" onClick={() => { setFilters(EMPTY); setApplied(EMPTY); }}>إعادة تعيين</Button>
        </Stack>
      </Paper>
      <DataTable
        columns={COLUMNS} rows={list.items} getKey={(r) => r.id} loading={list.loading} empty="لا توجد سجلات مطابقة"
        paging={{ page: list.page - 1, pageSize: list.pageSize, total: list.totalCount, onPage: (p) => list.setPage(p + 1), onPageSize: list.changePageSize }}
      />
    </>
  );
}

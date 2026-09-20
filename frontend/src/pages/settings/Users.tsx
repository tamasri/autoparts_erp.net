import { Alert, Avatar, Box, Chip, Stack, TextField } from '@mui/material';
import { usersApi } from '../../api/endpoints/users';
import { usePagedList } from '../../hooks/usePagedList';
import PageHeader from '../../components/ui/PageHeader';
import DataTable, { type Column } from '../../components/ui/DataTable';
import StatusChip from '../../components/ui/StatusChip';

type UserRow = {
  id: string;
  userName?: string;
  firstName?: string;
  lastName?: string;
  email?: string;
  /** The API returns role objects ({ roleId, code, name }). */
  roles?: Array<string | { roleId?: string; code?: string; name?: string }>;
  isActive?: boolean;
  isLockedOut?: boolean;
  lastLoginAtUtc?: string;
};

const roleCodes = (u: UserRow): string[] => (u.roles ?? []).map((r) => (typeof r === 'string' ? r : (r.code ?? r.name ?? ''))).filter(Boolean);

const COLUMNS: Column<UserRow>[] = [
  {
    header: 'المستخدم',
    render: (u) => (
      <Stack direction="row" gap={1.5} alignItems="center">
        <Avatar sx={{ width: 34, height: 34, bgcolor: 'primary.main', fontSize: 13 }}>{(u.userName ?? '?')[0].toUpperCase()}</Avatar>
        <Box><Box sx={{ fontWeight: 700 }}>{u.userName ?? '—'}</Box><Box sx={{ fontSize: 12, color: 'text.secondary' }}>{u.email}</Box></Box>
      </Stack>
    ),
  },
  { header: 'الاسم الكامل', render: (u) => [u.firstName, u.lastName].filter(Boolean).join(' ') || '—' },
  { header: 'الأدوار', render: (u) => <Stack direction="row" gap={0.5} flexWrap="wrap">{roleCodes(u).map((c) => <Chip key={c} size="small" variant="outlined" color="primary" label={c} />)}</Stack> },
  { header: 'الحالة', render: (u) => (u.isLockedOut ? <StatusChip status="LOCKED" /> : <StatusChip status={u.isActive === false ? 'INACTIVE' : 'ACTIVE'} />) },
  { header: 'آخر دخول', nowrap: true, render: (u) => (u.lastLoginAtUtc ? new Date(u.lastLoginAtUtc).toLocaleString('ar') : '—') },
];

export default function Users(): JSX.Element {
  const list = usePagedList<UserRow>({ errorMessage: 'تعذر تحميل المستخدمين', fetcher: ({ page, pageSize, search }) => usersApi.getUsers(page, pageSize, search) });
  const active = list.items.filter((u) => u.isActive ?? true).length;

  return (
    <>
      <PageHeader
        title="المستخدمون" subtitle="حسابات الدخول إلى النظام وأدوارها"
        actions={<><Chip color="success" variant="outlined" label={`نشط: ${active}`} /><Chip variant="outlined" label={`الإجمالي: ${list.totalCount}`} /></>}
      />
      {list.error ? <Alert severity="error" sx={{ mb: 2 }}>{list.error}</Alert> : null}
      <TextField size="small" placeholder="ابحث باسم المستخدم أو الاسم..." value={list.searchInput} onChange={(e) => list.setSearchInput(e.target.value)} sx={{ mb: 2, width: 340 }} />
      <DataTable
        columns={COLUMNS} rows={list.items} getKey={(u) => u.id} loading={list.loading} empty="لا يوجد مستخدمون"
        paging={{ page: list.page - 1, pageSize: list.pageSize, total: list.totalCount, onPage: (p) => list.setPage(p + 1), onPageSize: list.changePageSize }}
      />
    </>
  );
}

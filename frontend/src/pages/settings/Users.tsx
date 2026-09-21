import { useState } from 'react';
import { Alert, Avatar, Box, Button, Chip, MenuItem, Stack, TextField } from '@mui/material';
import { usersApi, type User } from '../../api/endpoints/users';
import { usePagedList } from '../../hooks/usePagedList';
import { useCan } from '../../hooks/useCan';
import PageHeader from '../../components/ui/PageHeader';
import DataTable, { type Column } from '../../components/ui/DataTable';
import StatusChip from '../../components/ui/StatusChip';
import UserDialog from '../../features/users/UserDialog';

const FILTERS = [{ key: 'all', label: 'الكل' }, { key: 'active', label: 'النشطون' }, { key: 'inactive', label: 'الموقوفون' }];

export default function Users(): JSX.Element {
  const canWrite = useCan('users.write');
  const [status, setStatus] = useState('all');
  const [editor, setEditor] = useState<{ open: boolean; user: User | null }>({ open: false, user: null });
  const list = usePagedList<User>({
    errorMessage: 'تعذر تحميل المستخدمين', deps: [status],
    fetcher: ({ page, pageSize, search }) => usersApi.getUsers(page, pageSize, search, status === 'all' ? undefined : status === 'active'),
  });

  const columns: Column<User>[] = [
    {
      header: 'المستخدم',
      render: (u) => (
        <Stack direction="row" gap={1.5} alignItems="center">
          <Avatar sx={{ width: 34, height: 34, bgcolor: 'primary.main', fontSize: 13 }}>{(u.userName || '?')[0].toUpperCase()}</Avatar>
          <Box><Box sx={{ fontWeight: 700 }}>{u.userName}</Box><Box sx={{ fontSize: 12, color: 'text.secondary' }}>{u.email}</Box></Box>
        </Stack>
      ),
    },
    { header: 'الاسم الكامل', render: (u) => [u.firstName, u.lastName].filter(Boolean).join(' ') || '—' },
    { header: 'الأدوار', render: (u) => <Stack direction="row" gap={0.5} flexWrap="wrap">{u.roles.map((r) => <Chip key={r.roleId} size="small" variant="outlined" color="primary" label={r.code} />)}</Stack> },
    { header: 'الحالة', render: (u) => (!u.isActive ? <StatusChip status="INACTIVE" /> : u.isLockedOut ? <StatusChip status="LOCKED" /> : <StatusChip status="ACTIVE" />) },
    { header: 'آخر دخول', nowrap: true, render: (u) => (u.lastLoginAtUtc ? new Date(u.lastLoginAtUtc).toLocaleString('ar') : '—') },
    { header: '', nowrap: true, render: (u) => (canWrite ? <Button size="small" onClick={() => setEditor({ open: true, user: u })}>تعديل</Button> : null) },
  ];

  return (
    <>
      <PageHeader
        title="المستخدمون" subtitle="حسابات الدخول إلى النظام وأدوارها وحالتها"
        actions={<><Chip variant="outlined" label={`الإجمالي: ${list.totalCount}`} />{canWrite ? <Button variant="contained" size="small" onClick={() => setEditor({ open: true, user: null })}>＋ مستخدم جديد</Button> : null}</>}
      />
      {list.error ? <Alert severity="error" sx={{ mb: 2 }}>{list.error}</Alert> : null}
      <Stack direction="row" gap={2} sx={{ mb: 2 }}>
        <TextField size="small" placeholder="ابحث باسم المستخدم أو الاسم..." value={list.searchInput} onChange={(e) => list.setSearchInput(e.target.value)} sx={{ width: 340 }} />
        <TextField select size="small" label="الحالة" value={status} onChange={(e) => setStatus(e.target.value)} sx={{ minWidth: 140 }}>
          {FILTERS.map((f) => <MenuItem key={f.key} value={f.key}>{f.label}</MenuItem>)}
        </TextField>
      </Stack>
      <DataTable
        columns={columns} rows={list.items} getKey={(u) => u.id} loading={list.loading} empty="لا يوجد مستخدمون"
        paging={{ page: list.page - 1, pageSize: list.pageSize, total: list.totalCount, onPage: (p) => list.setPage(p + 1), onPageSize: list.changePageSize }}
      />
      <UserDialog open={editor.open} user={editor.user} onClose={() => setEditor({ open: false, user: null })} onSaved={list.reload} />
    </>
  );
}

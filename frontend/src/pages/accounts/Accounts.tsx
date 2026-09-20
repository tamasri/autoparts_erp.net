/**
 * الحسابات — the one screen for everyone the business deals with. An account can play several roles (customer, vendor, sales
 * rep, carrier); a customer account additionally has a credit profile, opened from the same row.
 */
import { ARABIC_PAGINATION } from '../../lib/tablePagination';
import { useCallback, useEffect, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import {
  Alert, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Menu, MenuItem, Paper, Stack, Tab, Table,
  TableBody, TableCell, TableContainer, TableHead, TablePagination, TableRow, Tabs, TextField,
} from '@mui/material';
import { partiesApi } from '../../api/endpoints/parties';
import { customersApi } from '../../api/endpoints/customers';
import { unwrapNode, unwrapPaged } from '../../api/apiData';
import { extractApiError, toast } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';
import PageHeader from '../../components/ui/PageHeader';
import CustomerDialog from '../../features/customers/CustomerDialog';
import DeactivateDialog from '../../features/customers/DeactivateDialog';
import type { Customer } from '../../features/customers/queries';

type Account = {
  id: string; code: string; displayName: string; displayNameAr: string; isActive: boolean;
  hasCombinedStatement: boolean; activeTypeCodes: string[]; customerId?: string | null;
};

const ROLES: Record<string, { label: string; color: 'primary' | 'secondary' | 'success' | 'warning' }> = {
  CUSTOMER: { label: 'زبون', color: 'primary' }, VENDOR: { label: 'مورّد', color: 'secondary' },
  SALES_REP: { label: 'مندوب مبيعات', color: 'success' }, CARRIER: { label: 'ناقل', color: 'warning' },
};
const FILTERS = [{ key: '', label: 'الكل' }, { key: 'CUSTOMER', label: 'الزبائن' }, { key: 'VENDOR', label: 'الموردون' }, { key: 'SALES_REP', label: 'المندوبون' }, { key: 'CARRIER', label: 'الناقلون' }];
const OTHER_ROLES = ['VENDOR', 'SALES_REP', 'CARRIER'];

function NewAccountDialog({ open, onClose, onSaved }: { open: boolean; onClose: () => void; onSaved: () => void }): JSX.Element {
  const [nameAr, setNameAr] = useState('');
  const [nameEn, setNameEn] = useState('');
  const [tax, setTax] = useState('');
  const [roles, setRoles] = useState<string[]>(['VENDOR']);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  async function save(): Promise<void> {
    if (!nameAr.trim()) { setError('الاسم بالعربية مطلوب'); return; }
    if (roles.length === 0) { setError('اختر دوراً واحداً على الأقل'); return; }
    setSaving(true); setError('');
    try {
      const res = await partiesApi.createParty({ displayName: nameEn.trim() || nameAr.trim(), displayNameAr: nameAr.trim(), taxNumber: tax.trim() || undefined, initialTypeCodes: roles });
      notifyResult(res, 'تم إنشاء الحساب');
      setNameAr(''); setNameEn(''); setTax(''); onSaved(); onClose();
    } catch (e: unknown) { setError(extractApiError(e, 'تعذر إنشاء الحساب')); }
    finally { setSaving(false); }
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>حساب جديد</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          {error ? <Alert severity="error">{error}</Alert> : null}
          <TextField size="small" label="الاسم بالعربية *" value={nameAr} onChange={(e) => setNameAr(e.target.value)} />
          <TextField size="small" label="الاسم بالإنجليزية" value={nameEn} onChange={(e) => setNameEn(e.target.value)} inputProps={{ dir: 'ltr' }} />
          <TextField size="small" label="الرقم الضريبي" value={tax} onChange={(e) => setTax(e.target.value)} />
          <Box>
            {OTHER_ROLES.map((r) => (
              <FormControlLabel key={r} label={ROLES[r].label} control={<Checkbox size="small" checked={roles.includes(r)}
                onChange={(e) => setRoles((all) => (e.target.checked ? [...all, r] : all.filter((x) => x !== r)))} />} />
            ))}
          </Box>
          <Alert severity="info">لإنشاء زبون بملفه الائتماني استخدم «＋ زبون جديد».</Alert>
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={onClose}>إلغاء</Button><Button variant="contained" disabled={saving} onClick={() => void save()}>حفظ</Button></DialogActions>
    </Dialog>
  );
}

export default function Accounts(): JSX.Element {
  const [role, setRole] = useState('');
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(20);
  const [rows, setRows] = useState<Account[]>([]);
  const [total, setTotal] = useState(0);
  const [error, setError] = useState('');
  const [menu, setMenu] = useState<HTMLElement | null>(null);
  const [newOther, setNewOther] = useState(false);
  const [customerDialog, setCustomerDialog] = useState<{ open: boolean; edit: Customer | null }>({ open: false, edit: null });
  const [deactivating, setDeactivating] = useState<Customer | null>(null);

  useEffect(() => { const h = window.setTimeout(() => { setQuery(search.trim()); setPage(0); }, 350); return () => window.clearTimeout(h); }, [search]);

  const load = useCallback(async () => {
    try {
      const data = unwrapPaged<Account>((await partiesApi.getParties({ page: page + 1, pageSize, typeCode: role || undefined, searchTerm: query || undefined })).data);
      setRows(data.items); setTotal(data.totalCount); setError('');
    } catch (e: unknown) { setError(extractApiError(e, 'تعذر تحميل الحسابات')); }
  }, [page, pageSize, role, query]);

  useEffect(() => { void load(); }, [load]);

  async function withCustomer(customerId: string, then: (c: Customer) => void): Promise<void> {
    try { const c = unwrapNode<Customer>((await customersApi.getCustomerById(customerId)).data); if (c) then(c); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر تحميل ملف الزبون')); }
  }

  return (
    <Box>
      <PageHeader
        title="الحسابات"
        subtitle="الزبائن والموردون والمندوبون والناقلون في مكان واحد — الحساب الواحد قد يكون زبوناً ومورّداً"
        actions={(
          <>
            <Button variant="contained" size="small" onClick={(e) => setMenu(e.currentTarget)}>＋ حساب جديد</Button>
            <Menu anchorEl={menu} open={Boolean(menu)} onClose={() => setMenu(null)}>
              <MenuItem onClick={() => { setMenu(null); setCustomerDialog({ open: true, edit: null }); }}>زبون (بملفه الائتماني)</MenuItem>
              <MenuItem onClick={() => { setMenu(null); setNewOther(true); }}>مورّد / مندوب / ناقل</MenuItem>
            </Menu>
          </>
        )}
      />
      <Stack direction="row" gap={1} alignItems="center" flexWrap="wrap" sx={{ mb: 2 }}>
        <Tabs value={FILTERS.findIndex((f) => f.key === role)} onChange={(_, i: number) => { setRole(FILTERS[i].key); setPage(0); }}>{FILTERS.map((f) => <Tab key={f.key} label={f.label} />)}</Tabs>
        <TextField size="small" placeholder="بحث بالاسم..." value={search} onChange={(e) => setSearch(e.target.value)} sx={{ width: 260, mr: 'auto' }} />
      </Stack>
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}

      <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 3 }}>
        <Table size="small">
          <TableHead><TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
            <TableCell>الكود</TableCell><TableCell>الاسم</TableCell><TableCell>الأدوار</TableCell><TableCell>الحالة</TableCell><TableCell />
          </TableRow></TableHead>
          <TableBody>
            {rows.length === 0 ? <TableRow><TableCell colSpan={5} align="center" sx={{ py: 5, color: 'text.secondary' }}>لا توجد حسابات</TableCell></TableRow> : null}
            {rows.map((a) => (
              <TableRow key={a.id} hover sx={{ opacity: a.isActive ? 1 : 0.55 }}>
                <TableCell><Chip size="small" variant="outlined" label={a.code} sx={{ fontFamily: 'monospace' }} /></TableCell>
                <TableCell sx={{ fontWeight: 700 }}>{a.displayNameAr || a.displayName}</TableCell>
                <TableCell>
                  <Stack direction="row" gap={0.5} flexWrap="wrap">
                    {a.activeTypeCodes.map((t) => <Chip key={t} size="small" color={ROLES[t]?.color ?? 'default'} label={ROLES[t]?.label ?? t} />)}
                    {a.hasCombinedStatement ? <Chip size="small" variant="outlined" label="زبون ومورّد" /> : null}
                  </Stack>
                </TableCell>
                <TableCell><Chip size="small" variant="outlined" color={a.isActive ? 'success' : 'default'} label={a.isActive ? 'نشط' : 'غير نشط'} /></TableCell>
                <TableCell align="left" sx={{ whiteSpace: 'nowrap' }}>
                  <Button size="small" component={RouterLink} to={`/parties/${a.id}/statement`}>{a.hasCombinedStatement ? 'كشف مدمج' : 'كشف الحساب'}</Button>
                  {a.customerId ? (
                    <>
                      <Button size="small" component={RouterLink} to={`/customers/${a.customerId}`}>ملف الزبون</Button>
                      <Button size="small" onClick={() => void withCustomer(a.customerId as string, (c) => setCustomerDialog({ open: true, edit: c }))}>تعديل</Button>
                      {a.isActive ? <Button size="small" color="error" onClick={() => void withCustomer(a.customerId as string, setDeactivating)}>إيقاف</Button> : null}
                    </>
                  ) : null}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        <TablePagination component="div" count={total} page={page} rowsPerPage={pageSize} rowsPerPageOptions={[10, 20, 50, 100]}
          onPageChange={(_, p) => setPage(p)} onRowsPerPageChange={(e) => { setPageSize(Number(e.target.value)); setPage(0); }} {...ARABIC_PAGINATION} />
      </TableContainer>

      <NewAccountDialog open={newOther} onClose={() => setNewOther(false)} onSaved={() => void load()} />
      <CustomerDialog open={customerDialog.open} editId={customerDialog.edit?.id ?? null} initial={customerDialog.edit ?? undefined}
        onClose={() => { setCustomerDialog({ open: false, edit: null }); void load(); }} />
      <DeactivateDialog open={deactivating !== null} customer={deactivating} onClose={() => { setDeactivating(null); void load(); }} />
    </Box>
  );
}

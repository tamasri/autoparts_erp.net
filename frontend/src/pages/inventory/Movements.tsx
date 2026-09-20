/** Detailed item movements: every stock change from every module, filterable, with a running balance when one item is selected. */
import { ARABIC_PAGINATION } from '../../lib/tablePagination';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link as RouterLink, useSearchParams } from 'react-router-dom';
import {
  Alert, Autocomplete, Box, Button, Chip, Paper, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TablePagination, TableRow, TextField,
} from '@mui/material';
import { warehouseApi, type LocationOverview, type StockMovement } from '../../api/endpoints/warehouse';
import { unwrapList, unwrapPaged } from '../../api/apiData';
import { extractApiError } from '../../lib/toast';
import { num, ymd, type ExportDocument } from '../../lib/exportClient';
import PageHeader from '../../components/ui/PageHeader';
import ExportMenu from '../../components/ui/ExportMenu';

export const MOVEMENT_LABEL: Record<string, string> = {
  RECEIPT: 'استلام', STATUS_CHANGE: 'تخزين / تغيير حالة', TRANSFER_OUT: 'تحويل صادر', TRANSFER_IN: 'تحويل وارد',
  ADJUSTMENT: 'تسوية', SALE: 'بيع', SALE_RETURN: 'مرتجع مبيعات', ISSUE: 'صرف', PURCHASE: 'شراء', PURCHASE_VOID: 'إلغاء شراء',
};
const TYPES = Object.entries(MOVEMENT_LABEL);
const REF_ROUTE: Record<string, (id: string) => string> = { INVOICE: (id) => `/invoices/${id}`, PURCHASE_INVOICE: () => '/purchasing' };
const REF_LABEL: Record<string, string> = {
  INVOICE: 'فاتورة', RECEIVING: 'مستند استلام', PUTAWAY: 'تخزين', TRANSFER: 'تحويل', ADJUSTMENT: 'تسوية', INVENTORY_ADJUSTMENT: 'تسوية مباشرة',
  BATCH_RECEIPT: 'استلام دفعة', ISSUE_ORDER: 'أمر صرف', PURCHASE_INVOICE: 'فاتورة شراء',
};

const fmt = (v: number | null | undefined): string => (v === null || v === undefined ? '—' : v.toLocaleString('en-US', { maximumFractionDigits: 4 }));

export default function Movements(): JSX.Element {
  const [params] = useSearchParams();
  const [locations, setLocations] = useState<LocationOverview[]>([]);
  const [locationId, setLocationId] = useState(params.get('locationId') ?? '');
  const [itemId, setItemId] = useState(params.get('itemId') ?? '');
  const [type, setType] = useState('');
  const [direction, setDirection] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(50);
  const [rows, setRows] = useState<StockMovement[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => { warehouseApi.overview(false).then((r) => setLocations(unwrapList<LocationOverview>(r.data))).catch(() => undefined); }, []);
  useEffect(() => { const h = window.setTimeout(() => { setQuery(search.trim()); setPage(0); }, 350); return () => window.clearTimeout(h); }, [search]);

  const filters = useMemo(() => ({ itemId, locationId, movementType: type, direction, from, to, search: query }), [itemId, locationId, type, direction, from, to, query]);

  const load = useCallback(async () => {
    setLoading(true); setError('');
    try {
      const data = unwrapPaged<StockMovement>((await warehouseApi.movements({ ...filters, page: page + 1, pageSize })).data);
      setRows(data.items); setTotal(data.totalCount);
    } catch (e: unknown) { setError(extractApiError(e, 'تعذر تحميل حركة الأصناف')); }
    finally { setLoading(false); }
  }, [filters, page, pageSize]);

  useEffect(() => { void load(); }, [load]);

  const buildExport = async (): Promise<ExportDocument> => {
    const data = unwrapPaged<StockMovement>((await warehouseApi.movements({ ...filters, page: 1, pageSize: 500 })).data);
    return {
      title: 'حركة الأصناف', subtitle: `${data.totalCount} حركة${data.totalCount > 500 ? ' (أحدث 500)' : ''}`, fileName: 'stock-movements',
      fields: [
        { label: 'الموقع', value: locations.find((l) => l.id === locationId)?.name ?? 'الكل' },
        { label: 'من', value: from || '-' }, { label: 'إلى', value: to || '-' },
      ],
      tables: [{
        columns: ['التاريخ', 'الصنف', 'الاسم', 'الموقع', 'نوع الحركة', 'الاتجاه', 'الكمية', 'الرصيد التراكمي', 'المرجع', 'المستخدم', 'ملاحظات'],
        rows: data.items.map((m) => [ymd(m.createdAt), m.itemCode, m.itemName, m.locationCode, MOVEMENT_LABEL[m.movementType] ?? m.movementType, m.direction === 'IN' ? 'وارد' : 'صادر',
          num(m.direction === 'IN' ? m.qty : -m.qty), num(m.balanceAfter), REF_LABEL[m.referenceType ?? ''] ?? m.referenceType ?? '', m.performedBy ?? '', m.notes ?? '']),
        numericColumns: [6, 7],
      }],
    };
  };

  return (
    <Box>
      <PageHeader title="حركة الأصناف" subtitle="سجل تفصيلي لكل دخول وخروج من المخزون: من أي مستند ومتى ومن قام به" actions={<ExportMenu build={buildExport} disabled={loading} />} />
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}

      <Paper variant="outlined" sx={{ p: 2, mb: 2, borderRadius: 3 }}>
        <Stack direction="row" gap={2} flexWrap="wrap" alignItems="center">
          <TextField size="small" label="بحث (رمز / اسم / ملاحظة)" value={search} onChange={(e) => setSearch(e.target.value)} sx={{ minWidth: 220 }} />
          <Autocomplete
            size="small" sx={{ minWidth: 230 }} options={locations} value={locations.find((l) => l.id === locationId) ?? null}
            onChange={(_, v) => { setLocationId(v?.id ?? ''); setPage(0); }} getOptionLabel={(o) => `${o.code} — ${o.name}`} isOptionEqualToValue={(a, b) => a.id === b.id}
            renderInput={(p) => <TextField {...p} label="الموقع" />}
          />
          <TextField select size="small" label="نوع الحركة" value={type} onChange={(e) => { setType(e.target.value); setPage(0); }} sx={{ minWidth: 160 }} SelectProps={{ native: true }} InputLabelProps={{ shrink: true }}>
            <option value="">الكل</option>{TYPES.map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </TextField>
          <TextField select size="small" label="الاتجاه" value={direction} onChange={(e) => { setDirection(e.target.value); setPage(0); }} sx={{ minWidth: 110 }} SelectProps={{ native: true }} InputLabelProps={{ shrink: true }}>
            <option value="">الكل</option><option value="IN">وارد</option><option value="OUT">صادر</option>
          </TextField>
          <TextField size="small" type="date" label="من" value={from} onChange={(e) => { setFrom(e.target.value); setPage(0); }} InputLabelProps={{ shrink: true }} />
          <TextField size="small" type="date" label="إلى" value={to} onChange={(e) => { setTo(e.target.value); setPage(0); }} InputLabelProps={{ shrink: true }} />
          {itemId ? <Chip color="primary" label="مفلتر على صنف واحد (يظهر الرصيد التراكمي)" onDelete={() => { setItemId(''); setPage(0); }} /> : null}
        </Stack>
      </Paper>

      <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 3, opacity: loading ? 0.6 : 1 }}>
        <Table size="small">
          <TableHead>
            <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
              <TableCell>التاريخ</TableCell><TableCell>الصنف</TableCell><TableCell>الموقع</TableCell><TableCell>الحركة</TableCell>
              <TableCell align="left">الكمية</TableCell><TableCell align="left">الرصيد التراكمي</TableCell><TableCell>المرجع</TableCell><TableCell>المستخدم</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.length === 0 ? <TableRow><TableCell colSpan={8} align="center" sx={{ py: 5, color: 'text.secondary' }}>لا توجد حركات</TableCell></TableRow> : null}
            {rows.map((m) => {
              const to2 = m.referenceType && m.referenceId ? REF_ROUTE[m.referenceType]?.(m.referenceId) : undefined;
              return (
                <TableRow key={m.id} hover>
                  <TableCell sx={{ whiteSpace: 'nowrap' }}>{new Date(m.createdAt).toLocaleString('ar')}</TableCell>
                  <TableCell>
                    <Button size="small" sx={{ p: 0, minWidth: 0, fontFamily: 'monospace', fontWeight: 700 }} onClick={() => { setItemId(m.itemId); setPage(0); }}>{m.itemCode}</Button>
                    <Box component="span" sx={{ mx: 1, color: 'text.secondary', fontSize: 12 }}>{m.itemName}</Box>
                  </TableCell>
                  <TableCell>{m.locationCode}</TableCell>
                  <TableCell><Chip size="small" variant="outlined" color={m.direction === 'IN' ? 'success' : 'error'} label={MOVEMENT_LABEL[m.movementType] ?? m.movementType} /></TableCell>
                  <TableCell align="left" sx={{ fontWeight: 700, color: m.direction === 'IN' ? 'success.main' : 'error.main' }}>{m.direction === 'IN' ? '+' : '−'}{fmt(m.qty)}</TableCell>
                  <TableCell align="left">{fmt(m.balanceAfter)}</TableCell>
                  <TableCell>
                    {m.referenceType ? (to2 ? <Box component={RouterLink} to={to2} sx={{ color: 'primary.main', textDecoration: 'none' }}>{REF_LABEL[m.referenceType] ?? m.referenceType}</Box> : (REF_LABEL[m.referenceType] ?? m.referenceType)) : '—'}
                    {m.notes ? <Box sx={{ fontSize: 11, color: 'text.secondary' }}>{m.notes}</Box> : null}
                  </TableCell>
                  <TableCell>{m.performedBy ?? '—'}</TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
        <TablePagination component="div" count={total} page={page} rowsPerPage={pageSize} rowsPerPageOptions={[25, 50, 100, 200]}
          onPageChange={(_, p) => setPage(p)} onRowsPerPageChange={(e) => { setPageSize(Number(e.target.value)); setPage(0); }} {...ARABIC_PAGINATION} />
      </TableContainer>
    </Box>
  );
}

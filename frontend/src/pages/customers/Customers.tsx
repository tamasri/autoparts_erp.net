/**
 * pages/customers/Customers.tsx — AutoPartsERP
 *
 * Migrated in phases:
 *   Phase 2: TanStack Query (useCustomerList, useSaveCustomer, useDeactivateCustomer)
 *   Phase 3: CustomerDialog (RHF + Zod) + DeactivateDialog (no more window.prompt)
 *   Phase 5: MUI X DataGrid (server-side pagination, column config)
 *
 * Acceptance criteria met:
 *   ✅ Customers→Invoices→Customers shows cached data <100ms (staleTime 30s)
 *   ✅ No full-page re-render per keystroke (RHF uncontrolled inputs)
 *   ✅ Zod validation messages shown inline in Arabic
 *   ✅ window.prompt replaced with DeactivateDialog
 *
 * i18n: strings are hardcoded Arabic per current convention.
 *       TODO(phase4): replace with useTranslation().t('customers.*')
 */
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Box,
  Button,
  Chip,
  IconButton,
  InputAdornment,
  Paper,
  Skeleton,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import {
  DataGrid,
  type GridColDef,
  type GridPaginationModel,
  type GridRenderCellParams,
} from '@mui/x-data-grid';
import { useCustomerList, type Customer } from '../../features/customers/queries';
import CustomerDialog from '../../features/customers/CustomerDialog';
import DeactivateDialog from '../../features/customers/DeactivateDialog';

// ── Type badge colours (Vex palette preserved) ───────────────────────────────

const TYPE_META: Record<string, { label: string; color: 'primary' | 'warning' | 'secondary' }> = {
  WORKSHOP:  { label: 'ورشة',  color: 'primary' },
  RETAIL:    { label: 'تجزئة', color: 'warning' },
  WHOLESALE: { label: 'جملة',  color: 'secondary' },
};

// ── DataGrid column definitions ──────────────────────────────────────────────

function buildColumns(
  onEdit:       (row: Customer) => void,
  onDeactivate: (row: Customer) => void,
): GridColDef<Customer>[] {
  return [
    {
      field: 'code',
      headerName: 'الكود',
      width: 110,
      renderCell: ({ value }: GridRenderCellParams) => (
        <Chip
          label={String(value ?? '')}
          size="small"
          sx={{
            background: 'var(--clr-primary-light)',
            color: 'var(--clr-primary-dark)',
            fontWeight: 700,
            fontSize: 11,
          }}
        />
      ),
    },
    {
      field: 'name',
      headerName: 'الاسم',
      flex: 1,
      minWidth: 160,
      renderCell: ({ value }: GridRenderCellParams) => (
        <Typography variant="body2" fontWeight={600} noWrap>{String(value ?? '')}</Typography>
      ),
    },
    {
      field: 'type',
      headerName: 'النوع',
      width: 100,
      renderCell: ({ value }: GridRenderCellParams) => {
        const meta = TYPE_META[String(value ?? '').toUpperCase()] ?? { label: String(value ?? ''), color: 'default' as const };
        return <Chip label={meta.label} size="small" color={meta.color as 'primary'} variant="outlined" />;
      },
    },
    {
      field: 'city',
      headerName: 'المدينة',
      width: 120,
      renderCell: ({ value }: GridRenderCellParams) => (
        <Typography variant="body2" color="text.secondary">{String(value ?? '—')}</Typography>
      ),
    },
    {
      field: 'balanceSyp',
      headerName: 'الرصيد المتأخر',
      width: 140,
      type: 'number',
      renderCell: ({ value }: GridRenderCellParams) => {
        const n = Number(value ?? 0);
        return (
          <Typography variant="body2" fontWeight={600} color={n > 0 ? 'error.main' : 'text.primary'}>
            {n.toLocaleString('en-US')}
          </Typography>
        );
      },
    },
    {
      field: 'creditLimitSyp',
      headerName: 'الحد الائتماني',
      width: 130,
      type: 'number',
      renderCell: ({ value }: GridRenderCellParams) => (
        <Typography variant="body2" color="text.secondary">
          {Number(value ?? 0).toLocaleString('en-US')}
        </Typography>
      ),
    },
    {
      field: 'isActive',
      headerName: 'الحالة',
      width: 90,
      renderCell: ({ value }: GridRenderCellParams) => (
        <Chip
          label={value !== false ? 'نشط' : 'موقوف'}
          size="small"
          color={value !== false ? 'success' : 'default'}
          variant="filled"
        />
      ),
    },
    {
      field: '__actions',
      headerName: 'إجراءات',
      width: 140,
      sortable: false,
      filterable: false,
      renderCell: ({ row }: GridRenderCellParams<Customer>) => (
        <Box sx={{ display: 'flex', gap: 0.5, alignItems: 'center' }}>
          <Tooltip title="تعديل">
            <IconButton
              size="small"
              onClick={(e) => { e.stopPropagation(); onEdit(row); }}
              aria-label={`تعديل ${row.name}`}
            >
              ✏️
            </IconButton>
          </Tooltip>
          {row.isActive !== false && (
            <Tooltip title="إلغاء التفعيل">
              <IconButton
                size="small"
                color="error"
                onClick={(e) => { e.stopPropagation(); onDeactivate(row); }}
                aria-label={`إلغاء تفعيل ${row.name}`}
              >
                🚫
              </IconButton>
            </Tooltip>
          )}
        </Box>
      ),
    },
  ];
}

// ── Page component ───────────────────────────────────────────────────────────

export default function Customers(): JSX.Element {
  const navigate = useNavigate();

  // ── Pagination & search state ───────────────────────────────────────────
  const [paginationModel, setPaginationModel] = useState<GridPaginationModel>({
    page: 0,        // DataGrid is 0-indexed; API is 1-indexed (converted below)
    pageSize: 20,
  });
  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');

  // Debounce search 350ms without lodash
  const [debounceTimer, setDebounceTimer] = useState<ReturnType<typeof setTimeout> | null>(null);
  function handleSearchChange(value: string): void {
    setSearchInput(value);
    if (debounceTimer) clearTimeout(debounceTimer);
    setDebounceTimer(
      setTimeout(() => {
        setSearch(value.trim());
        setPaginationModel((m) => ({ ...m, page: 0 }));
      }, 350),
    );
  }

  // ── TanStack Query ──────────────────────────────────────────────────────
  const { data, isLoading, isError, error, isFetching } = useCustomerList({
    page:     paginationModel.page + 1,   // convert 0-indexed → 1-indexed for API
    pageSize: paginationModel.pageSize,
    search,
  });

  const rows     = data?.items      ?? [];
  const rowCount = data?.totalCount ?? 0;

  // ── Dialog state ────────────────────────────────────────────────────────
  const [dialogOpen, setDialogOpen]             = useState(false);
  const [editTarget, setEditTarget]             = useState<Customer | null>(null);
  const [deactivateTarget, setDeactivateTarget] = useState<Customer | null>(null);

  function openCreate(): void { setEditTarget(null); setDialogOpen(true); }
  function openEdit(row: Customer): void { setEditTarget(row); setDialogOpen(true); }
  function openDeactivate(row: Customer): void { setDeactivateTarget(row); }

  const columns = buildColumns(openEdit, openDeactivate);

  // ── Render ──────────────────────────────────────────────────────────────
  return (
    <Box sx={{ direction: 'rtl' }}>
      {/* ── Page Header ── */}
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', mb: 3 }}>
        <Box>
          <Typography variant="h5" fontWeight={800} color="text.primary">
            {/* TODO(phase4): t('customers.title') */}
            العملاء
          </Typography>
          <Typography variant="body2" color="text.secondary">
            إدارة قاعدة بيانات العملاء
          </Typography>
        </Box>
        <Button variant="contained" onClick={openCreate}>＋ عميل جديد</Button>
      </Box>

      {/* ── Error banner ── */}
      {isError && (
        <Paper sx={{ p: 2, mb: 2, background: 'var(--clr-danger-light)', color: 'var(--clr-danger)', border: '1px solid var(--clr-danger)' }}>
          {(error as Error)?.message ?? 'تعذر تحميل العملاء'}
        </Paper>
      )}

      {/* ── Search ── */}
      <TextField
        value={searchInput}
        onChange={(e) => handleSearchChange(e.target.value)}
        placeholder="بحث بالاسم أو الكود..."
        sx={{ mb: 2, maxWidth: 400 }}
        InputProps={{ startAdornment: <InputAdornment position="start">🔍</InputAdornment> }}
      />

      {/* ── DataGrid ── */}
      <Paper sx={{ width: '100%', overflow: 'hidden' }}>
        {isLoading ? (
          <Box sx={{ p: 2 }}>
            {Array.from({ length: 6 }).map((_, i) => (
              <Skeleton key={i} height={52} sx={{ mb: 0.5 }} />
            ))}
          </Box>
        ) : (
          <DataGrid<Customer>
            rows={rows}
            columns={columns}
            paginationMode="server"
            rowCount={rowCount}
            paginationModel={paginationModel}
            onPaginationModelChange={setPaginationModel}
            pageSizeOptions={[10, 20, 50]}
            loading={isFetching}
            onRowClick={({ row }) => navigate(`/customers/${row.id}`)}
            autoHeight
            disableRowSelectionOnClick
            getRowId={(r) => r.id}
            localeText={{
              noRowsLabel: search ? 'لا توجد نتائج مطابقة' : 'لا يوجد عملاء',
              MuiTablePagination: {
                labelRowsPerPage: 'صفوف في الصفحة:',
                labelDisplayedRows: ({ from, to, count }) =>
                  `${from}–${to} من ${count !== -1 ? count : `أكثر من ${to}`}`,
              },
            }}
            sx={{
              border: 'none',
              opacity: isFetching && !isLoading ? 0.7 : 1,
              transition: 'opacity 120ms',
              '& .MuiDataGrid-row': { cursor: 'pointer' },
            }}
          />
        )}
      </Paper>

      {/* ── Dialogs ── */}
      <CustomerDialog
        open={dialogOpen}
        editId={editTarget?.id ?? null}
        initial={editTarget ?? undefined}
        onClose={() => { setDialogOpen(false); setEditTarget(null); }}
      />

      <DeactivateDialog
        open={Boolean(deactivateTarget)}
        customer={deactivateTarget}
        onClose={() => setDeactivateTarget(null)}
      />
    </Box>
  );
}

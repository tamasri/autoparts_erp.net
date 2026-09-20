/** Current stock: one row per item per location (any number of warehouses), filterable by location and search. */
import { useEffect, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Autocomplete, Button, Chip, Stack, TextField } from '@mui/material';
import { inventoryApi } from '../../api/endpoints/inventory';
import { warehouseApi, type LocationOverview } from '../../api/endpoints/warehouse';
import { unwrapList } from '../../api/apiData';
import { usePagedList } from '../../hooks/usePagedList';
import { num, type ExportDocument } from '../../lib/exportClient';
import PageHeader from '../../components/ui/PageHeader';
import DataTable, { type Column } from '../../components/ui/DataTable';
import ExportMenu from '../../components/ui/ExportMenu';

type StockRow = {
  id: string; skuId: string; skuCode: string; skuName: string; locationId: string; locationCode: string;
  quantityOnHand: number; quantityReserved: number; quantityAvailable: number; lowStockFlag: boolean;
};

const fmt = (v: number): string => Number(v ?? 0).toLocaleString('en-US', { maximumFractionDigits: 4 });

export default function Inventory(): JSX.Element {
  const [locations, setLocations] = useState<LocationOverview[]>([]);
  const [locationId, setLocationId] = useState('');
  useEffect(() => { warehouseApi.overview(false).then((r) => setLocations(unwrapList<LocationOverview>(r.data))).catch(() => undefined); }, []);

  const list = usePagedList<StockRow>({
    errorMessage: 'تعذر تحميل المخزون',
    pageSize: 25,
    deps: [locationId],
    fetcher: ({ page, pageSize, search }) => inventoryApi.getStock({ page, pageSize, searchTerm: search || undefined, locationId: locationId || undefined }),
  });

  const buildExport = async (): Promise<ExportDocument> => {
    const res = await inventoryApi.getStock({ page: 1, pageSize: 200, searchTerm: list.searchInput.trim() || undefined, locationId: locationId || undefined });
    const rows = unwrapList<StockRow>(res.data);
    return {
      title: 'المخزون الحالي', subtitle: locations.find((l) => l.id === locationId)?.name ?? 'كل المواقع', fileName: 'stock', fields: [],
      tables: [{
        columns: ['الرمز', 'الصنف', 'الموقع', 'الموجود', 'المحجوز', 'المتاح'],
        rows: rows.map((r) => [r.skuCode, r.skuName, r.locationCode, num(r.quantityOnHand), num(r.quantityReserved), num(r.quantityAvailable)]),
        numericColumns: [3, 4, 5],
      }],
    };
  };

  const columns: Column<StockRow>[] = [
    { header: 'الرمز', render: (r) => <Chip size="small" variant="outlined" label={r.skuCode} sx={{ fontFamily: 'monospace', fontWeight: 700 }} /> },
    { header: 'الصنف', render: (r) => r.skuName },
    { header: 'الموقع', render: (r) => r.locationCode },
    { header: 'الموجود', numeric: true, render: (r) => <strong>{fmt(r.quantityOnHand)}</strong> },
    { header: 'المحجوز', numeric: true, render: (r) => fmt(r.quantityReserved) },
    { header: 'المتاح', numeric: true, render: (r) => <strong style={{ color: r.quantityAvailable > 0 ? undefined : 'crimson' }}>{fmt(r.quantityAvailable)}</strong> },
    { header: 'الحالة', render: (r) => (r.quantityAvailable <= 0 ? <Chip size="small" color="error" label="نافد" /> : r.lowStockFlag ? <Chip size="small" color="warning" label="منخفض" /> : <Chip size="small" color="success" variant="outlined" label="متوفر" />) },
    { header: '', render: (r) => <Button size="small" component={RouterLink} to={`/inventory/movements?locationId=${r.locationId}`}>الحركة</Button> },
  ];

  return (
    <>
      <PageHeader title="المخزون" subtitle="الكميات الحالية لكل صنف في كل موقع" actions={<ExportMenu build={buildExport} />} />
      {list.error ? <Alert severity="error" sx={{ mb: 2 }}>{list.error}</Alert> : null}
      <Stack direction="row" gap={2} flexWrap="wrap" sx={{ mb: 2 }}>
        <TextField size="small" placeholder="ابحث برمز الصنف أو اسمه..." value={list.searchInput} onChange={(e) => list.setSearchInput(e.target.value)} sx={{ width: 320 }} />
        <Autocomplete
          size="small" sx={{ width: 260 }} options={locations} value={locations.find((l) => l.id === locationId) ?? null}
          onChange={(_, v) => setLocationId(v?.id ?? '')} getOptionLabel={(o) => `${o.code} — ${o.name}`} isOptionEqualToValue={(a, b) => a.id === b.id}
          renderInput={(p) => <TextField {...p} label="الموقع" />}
        />
      </Stack>
      <DataTable
        columns={columns} rows={list.items} getKey={(r) => r.id} loading={list.loading} empty="لا توجد أرصدة"
        paging={{ page: list.page - 1, pageSize: list.pageSize, total: list.totalCount, onPage: (p) => list.setPage(p + 1), onPageSize: list.changePageSize }}
      />
    </>
  );
}

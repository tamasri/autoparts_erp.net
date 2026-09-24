import { useState } from 'react';
import { Link as RouterLink, useNavigate } from 'react-router-dom';
import { Alert, Button, Chip, Stack, Tab, Tabs, TextField } from '@mui/material';
import { invoicesApi } from '../../api/endpoints/invoices';
import { unwrapPaged } from '../../api/apiData';
import { usePagedList } from '../../hooks/usePagedList';
import { num, ymd, type ExportDocument } from '../../lib/exportClient';
import PageHeader from '../../components/ui/PageHeader';
import DataTable, { type Column } from '../../components/ui/DataTable';
import ExportMenu from '../../components/ui/ExportMenu';
import StatusChip from '../../components/ui/StatusChip';
import Money from '../../components/ui/Money';

type Invoice = {
  id: string; invoiceNumber?: string; customerName?: string; invoiceDate?: string; dueDate?: string;
  totalSyp?: number; totalUsd?: number; balanceSyp?: number; balanceUsd?: number; status?: string; type?: string;
};

const FILTERS = [{ key: '', label: 'الكل' }, { key: 'DRAFT', label: 'مسودة' }, { key: 'CONFIRMED', label: 'مؤكدة' }, { key: 'POSTED', label: 'مرحّلة' }, { key: 'VOID', label: 'ملغاة' }];

export default function Invoices(): JSX.Element {
  const navigate = useNavigate();
  const [status, setStatus] = useState('');
  const list = usePagedList<Invoice>({
    errorMessage: 'تعذر تحميل الفواتير',
    deps: [status],
    fetcher: ({ page, pageSize, search }) => invoicesApi.getInvoices({ page, pageSize, status: status || undefined, searchTerm: search || undefined }),
  });
  const today = new Date();

  const buildExport = async (): Promise<ExportDocument> => {
    const data = unwrapPaged<Invoice>((await invoicesApi.getInvoices({ page: 1, pageSize: 100, status: status || undefined, searchTerm: list.searchInput.trim() || undefined })).data);
    return {
      title: 'فواتير المبيعات', subtitle: `${FILTERS.find((f) => f.key === status)?.label} — ${data.totalCount} فاتورة${data.totalCount > 100 ? ' (أول 100)' : ''}`, fileName: 'invoices', fields: [],
      tables: [{
        columns: ['رقم الفاتورة', 'الزبون', 'التاريخ', 'الاستحقاق', 'الإجمالي (ل.س)', 'الإجمالي ($)', 'الحالة'],
        rows: data.items.map((i) => [i.invoiceNumber ?? '', i.customerName ?? '', ymd(i.invoiceDate), ymd(i.dueDate), num(i.totalSyp), num(i.totalUsd), i.status ?? '']),
        numericColumns: [4, 5],
      }],
    };
  };

  const columns: Column<Invoice>[] = [
    {
      header: 'رقم الفاتورة',
      render: (i) => (
        <>
          <Button size="small" component={RouterLink} to={`/invoices/${i.id}`} sx={{ fontWeight: 700 }}>{i.invoiceNumber ?? i.id.slice(0, 8)}</Button>
          {i.type === 'RETURN' ? <Chip size="small" color="warning" label="مرتجع" /> : i.type === 'CREDIT_NOTE' ? <Chip size="small" label="إشعار دائن" /> : null}
        </>
      ),
    },
    { header: 'الزبون', render: (i) => i.customerName ?? '—' },
    { header: 'التاريخ', render: (i) => i.invoiceDate ?? '—' },
    {
      header: 'الاستحقاق',
      render: (i) => {
        const overdue = Boolean(i.dueDate && new Date(i.dueDate) < today && (i.status ?? '').toUpperCase() === 'POSTED' && Number(i.balanceSyp ?? 0) > 0);
        return overdue ? <Chip size="small" color="error" label={i.dueDate} /> : (i.dueDate ?? '—');
      },
    },
    { header: 'الإجمالي', numeric: true, render: (i) => <Money usd={i.totalUsd} syp={i.totalSyp} fontWeight={700} /> },
    { header: 'المتبقي', numeric: true, render: (i) => <Money usd={i.balanceUsd} syp={i.balanceSyp} fontWeight={700} color={Number(i.balanceUsd ?? i.balanceSyp ?? 0) > 0 ? 'error.main' : undefined} /> },
    { header: 'الحالة', render: (i) => <StatusChip status={i.status} /> },
  ];

  return (
    <>
      <PageHeader title="فواتير المبيعات" subtitle="إدارة وتتبع فواتير المبيعات"
        actions={<><ExportMenu build={buildExport} /><Button variant="contained" size="small" onClick={() => navigate('/invoices/new')}>＋ فاتورة جديدة</Button></>} />
      {list.error ? <Alert severity="error" sx={{ mb: 2 }}>{list.error}</Alert> : null}
      <Stack direction="row" gap={1} alignItems="center" flexWrap="wrap" sx={{ mb: 2 }}>
        <Tabs value={FILTERS.findIndex((f) => f.key === status)} onChange={(_, i: number) => setStatus(FILTERS[i].key)}>{FILTERS.map((f) => <Tab key={f.key} label={f.label} />)}</Tabs>
        <TextField size="small" placeholder="بحث برقم الفاتورة أو الزبون..." value={list.searchInput} onChange={(e) => list.setSearchInput(e.target.value)} sx={{ width: 300, mr: 'auto' }} />
      </Stack>
      <DataTable
        columns={columns} rows={list.items} getKey={(i) => i.id} loading={list.loading} empty="لا توجد فواتير لهذه الفئة"
        paging={{ page: list.page - 1, pageSize: list.pageSize, total: list.totalCount, onPage: (p) => list.setPage(p + 1), onPageSize: list.changePageSize }}
      />
    </>
  );
}

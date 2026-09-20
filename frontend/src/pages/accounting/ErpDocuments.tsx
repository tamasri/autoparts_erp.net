/** Read-only browser of what ERPNext holds (invoices, payments, journal entries, ledger, master data), linked to our records. */
import { ARABIC_PAGINATION } from '../../lib/tablePagination';
import { useCallback, useEffect, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Box, Chip, CircularProgress, Paper, Tab, Table, TableBody, TableCell, TableContainer, TableHead, TablePagination, TableRow, Tabs, TextField } from '@mui/material';
import { erpnextBrowseApi } from '../../api/endpoints/erpnextBrowse';
import { unwrapNode } from '../../api/apiData';
import { extractApiError } from '../../lib/toast';
import type { ExportDocument } from '../../lib/exportClient';
import PageHeader from '../../components/ui/PageHeader';
import ExportMenu from '../../components/ui/ExportMenu';

type Row = Record<string, unknown> & { name: string; localEntityType?: string; localEntityId?: string };
type Page = { columns: string[]; items: Row[]; totalCount: number };

const TABS: Array<{ doctype: string; label: string }> = [
  { doctype: 'Sales Invoice', label: 'فواتير المبيعات' },
  { doctype: 'Purchase Invoice', label: 'فواتير الشراء' },
  { doctype: 'Payment Entry', label: 'سندات الدفع/القبض' },
  { doctype: 'Journal Entry', label: 'قيود اليومية' },
  { doctype: 'GL Entry', label: 'دفتر الأستاذ' },
  { doctype: 'Customer', label: 'العملاء' },
  { doctype: 'Supplier', label: 'الموردون' },
  { doctype: 'Item', label: 'الأصناف' },
];

const COLUMN_LABEL: Record<string, string> = {
  name: 'الرقم', customer: 'العميل', supplier: 'المورد', party: 'الجهة', party_type: 'نوع الجهة', posting_date: 'التاريخ', due_date: 'الاستحقاق',
  currency: 'العملة', grand_total: 'الإجمالي', outstanding_amount: 'المتبقي', status: 'الحالة', is_return: 'مرتجع', payment_type: 'النوع', paid_amount: 'المبلغ',
  mode_of_payment: 'طريقة الدفع', voucher_type: 'نوع القيد', total_debit: 'مدين', total_credit: 'دائن', user_remark: 'ملاحظات', docstatus: 'docstatus', account: 'الحساب',
  debit: 'مدين', credit: 'دائن', voucher_no: 'المستند', remarks: 'ملاحظات', customer_name: 'الاسم', customer_group: 'المجموعة', territory: 'المنطقة', disabled: 'معطّل',
  supplier_name: 'الاسم', supplier_group: 'المجموعة', item_name: 'الاسم', item_group: 'المجموعة', stock_uom: 'الوحدة',
};

const LOCAL_ROUTE: Record<string, (id: string) => string | null> = {
  Invoice: (id) => `/invoices/${id}`,
  PurchaseInvoice: () => '/purchasing',
};

const cell = (v: unknown): string => (v === null || v === undefined ? '' : typeof v === 'number' ? v.toLocaleString('en-US', { maximumFractionDigits: 2 }) : String(v));

export default function ErpDocuments(): JSX.Element {
  const [tab, setTab] = useState(0);
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(20);
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [data, setData] = useState<Page | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const doctype = TABS[tab].doctype;

  useEffect(() => { const h = window.setTimeout(() => { setQuery(search.trim()); setPage(0); }, 350); return () => window.clearTimeout(h); }, [search]);

  const load = useCallback(async () => {
    setLoading(true); setError('');
    try {
      const res = await erpnextBrowseApi.documents(doctype, page + 1, pageSize, query);
      setData(unwrapNode<Page>(res.data));
    } catch (e: unknown) {
      setError(extractApiError(e, 'تعذر قراءة المستندات من ERPNext'));
      setData(null);
    } finally {
      setLoading(false);
    }
  }, [doctype, page, pageSize, query]);

  useEffect(() => { void load(); }, [load]);

  const columns = data?.columns ?? [];
  const buildExport = async (): Promise<ExportDocument> => {
    const res = await erpnextBrowseApi.documents(doctype, 1, 100, query);
    const d = unwrapNode<Page>(res.data);
    return {
      title: TABS[tab].label, subtitle: 'من ERPNext (أول 100 سجل حسب البحث)', fileName: `erpnext-${doctype.replace(/\s+/g, '-')}`, fields: [],
      tables: [{ columns: (d?.columns ?? []).map((c) => COLUMN_LABEL[c] ?? c), rows: (d?.items ?? []).map((r) => (d?.columns ?? []).map((c) => cell(r[c]))) }],
    };
  };

  return (
    <Box>
      <PageHeader title="مستندات ERPNext" subtitle="قراءة فقط — كل مستند مرتبط بسجله في نظامنا عند توفره" actions={<ExportMenu build={buildExport} disabled={!data} />} />
      <Tabs value={tab} onChange={(_, v: number) => { setTab(v); setPage(0); }} variant="scrollable" scrollButtons="auto" sx={{ mb: 2 }}>
        {TABS.map((t) => <Tab key={t.doctype} label={t.label} />)}
      </Tabs>
      <TextField size="small" placeholder="بحث بالرقم..." value={search} onChange={(e) => setSearch(e.target.value)} sx={{ mb: 2, width: 280 }} />
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 3, opacity: loading ? 0.6 : 1 }}>
        <Table size="small">
          <TableHead>
            <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
              {columns.map((c) => <TableCell key={c}>{COLUMN_LABEL[c] ?? c}</TableCell>)}
              <TableCell>في نظامنا</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {loading && !data ? <TableRow><TableCell colSpan={columns.length + 1} align="center"><CircularProgress size={24} /></TableCell></TableRow> : null}
            {data && data.items.length === 0 ? <TableRow><TableCell colSpan={columns.length + 1} align="center" sx={{ py: 4, color: 'text.secondary' }}>لا توجد مستندات</TableCell></TableRow> : null}
            {data?.items.map((r) => {
              const to = r.localEntityType && r.localEntityId ? LOCAL_ROUTE[r.localEntityType]?.(r.localEntityId) ?? null : null;
              return (
                <TableRow key={r.name} hover>
                  {columns.map((c) => <TableCell key={c} sx={c === 'name' ? { fontWeight: 600 } : undefined}>{cell(r[c])}</TableCell>)}
                  <TableCell>
                    {r.localEntityType
                      ? (to ? <Chip size="small" color="success" variant="outlined" component={RouterLink} to={to} clickable label={r.localEntityType} /> : <Chip size="small" color="success" variant="outlined" label={r.localEntityType} />)
                      : <Chip size="small" variant="outlined" label="غير مرتبط" />}
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
        <TablePagination component="div" count={data?.totalCount ?? 0} page={page} rowsPerPage={pageSize} rowsPerPageOptions={[10, 20, 50, 100]}
          onPageChange={(_, p) => setPage(p)} onRowsPerPageChange={(e) => { setPageSize(Number(e.target.value)); setPage(0); }} {...ARABIC_PAGINATION} />
      </TableContainer>
    </Box>
  );
}

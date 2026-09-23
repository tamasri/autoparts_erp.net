/** الذمم — who owes us (customers) and whom we owe (suppliers), with unpaid invoices aged by how late they are. */
import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Button, Chip, LinearProgress, Stack, TextField, Typography } from '@mui/material';
import { accountingApi, type PartyBalance, type PartyBalances as Balances } from '../../api/endpoints/accounting';
import { unwrapNode } from '../../api/apiData';
import { useLoad } from '../../hooks/useLoad';
import { today } from '../../lib/money';
import PageHeader from '../../components/ui/PageHeader';
import RoutedTabs from '../../components/ui/RoutedTabs';
import DataTable, { type Column } from '../../components/ui/DataTable';
import ExportMenu from '../../components/ui/ExportMenu';
import { partyBalancesDocument } from '../../features/accounting/documents';
import Money from '../../components/ui/Money';

function Panel({ role }: { role: 'CUSTOMER' | 'VENDOR' }): JSX.Element {
  const [asOf, setAsOf] = useState(today());
  const { data, loading, error } = useLoad<Balances | null>(async () => unwrapNode<Balances>((await accountingApi.partyBalances(role, asOf)).data), [role, asOf], 'تعذر تحميل الذمم');
  const customers = role === 'CUSTOMER';

  const columns: Column<PartyBalance>[] = [
    {
      header: customers ? 'الزبون' : 'المورّد',
      render: (r) => r.partyId
        ? <Button size="small" component={RouterLink} to={`/parties/${r.partyId}/statement`} sx={{ fontWeight: 700 }}>{r.party}</Button>
        : <strong>{r.party}</strong>,
    },
    { header: customers ? 'المستحق لنا' : 'المستحق علينا', numeric: true, render: (r) => <Money usd={r.balance} fontWeight={700} /> },
    { header: 'غير مستحق', numeric: true, render: (r) => (r.current ? <Money usd={r.current} /> : '') },
    { header: '1–30 يوماً', numeric: true, render: (r) => (r.days1To30 ? <Money usd={r.days1To30} /> : '') },
    { header: '31–60', numeric: true, render: (r) => (r.days31To60 ? <Money usd={r.days31To60} /> : '') },
    { header: '61–90', numeric: true, render: (r) => (r.days61To90 ? <Money usd={r.days61To90} /> : '') },
    { header: 'أكثر من 90', numeric: true, render: (r) => (r.over90 ? <Money usd={r.over90} fontWeight={700} color="error.main" /> : '') },
    { header: 'غير مخصص', numeric: true, render: (r) => (r.unallocated ? <Money usd={r.unallocated} /> : '') },
  ];

  return (
    <Stack spacing={2}>
      <Stack direction="row" gap={1.5} alignItems="center" flexWrap="wrap">
        <TextField size="small" type="date" label="كما في" value={asOf} onChange={(e) => setAsOf(e.target.value)} InputLabelProps={{ shrink: true }} />
        {data ? <Chip color="primary" label={<>الإجمالي: <Money usd={data.total} inline variant="body2" /></>} /> : null}
        <Typography variant="caption" color="text.secondary">«غير مخصص» = دفعات أو قيود على الحساب لم تُربط بفاتورة معيّنة (سالب = دفعة مقدّمة).</Typography>
        <span style={{ marginInlineStart: 'auto' }}><ExportMenu build={async () => partyBalancesDocument(data!)} disabled={!data} /></span>
      </Stack>
      {loading ? <LinearProgress /> : null}
      {error ? <Alert severity="error">{error}</Alert> : null}
      <DataTable columns={columns} rows={data?.rows ?? []} getKey={(r) => r.party} loading={loading} empty={customers ? 'لا توجد ذمم مدينة' : 'لا توجد ذمم دائنة'} />
    </Stack>
  );
}

export default function PartyBalancesPage(): JSX.Element {
  return (
    <>
      <PageHeader title="الذمم" subtitle="الذمم المدينة (الزبائن) والدائنة (الموردون) من دفتر الأستاذ، مع أعمار الديون" />
      <RoutedTabs tabs={[
        { key: 'receivables', label: 'الذمم المدينة — الزبائن', content: <Panel role="CUSTOMER" /> },
        { key: 'payables', label: 'الذمم الدائنة — الموردون', content: <Panel role="VENDOR" /> },
      ]} />
    </>
  );
}

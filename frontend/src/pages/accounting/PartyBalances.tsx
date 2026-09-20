/** الذمم — who owes us (customers) and whom we owe (suppliers), with unpaid invoices aged by how late they are. */
import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Button, Chip, LinearProgress, Stack, TextField, Typography } from '@mui/material';
import { accountingApi, type PartyBalance, type PartyBalances as Balances } from '../../api/endpoints/accounting';
import { unwrapNode } from '../../api/apiData';
import { useLoad } from '../../hooks/useLoad';
import { money, moneyOrBlank, today } from '../../lib/money';
import PageHeader from '../../components/ui/PageHeader';
import RoutedTabs from '../../components/ui/RoutedTabs';
import DataTable, { type Column } from '../../components/ui/DataTable';
import ExportMenu from '../../components/ui/ExportMenu';
import { partyBalancesDocument } from '../../features/accounting/documents';

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
    { header: customers ? 'المستحق لنا ($)' : 'المستحق علينا ($)', numeric: true, render: (r) => <strong>{money(r.balance)}</strong> },
    { header: 'غير مستحق', numeric: true, render: (r) => moneyOrBlank(r.current) },
    { header: '1–30 يوماً', numeric: true, render: (r) => moneyOrBlank(r.days1To30) },
    { header: '31–60', numeric: true, render: (r) => moneyOrBlank(r.days31To60) },
    { header: '61–90', numeric: true, render: (r) => moneyOrBlank(r.days61To90) },
    { header: 'أكثر من 90', numeric: true, render: (r) => (r.over90 ? <span style={{ color: 'crimson', fontWeight: 700 }}>{money(r.over90)}</span> : '') },
    { header: 'غير مخصص', numeric: true, render: (r) => moneyOrBlank(r.unallocated) },
  ];

  return (
    <Stack spacing={2}>
      <Stack direction="row" gap={1.5} alignItems="center" flexWrap="wrap">
        <TextField size="small" type="date" label="كما في" value={asOf} onChange={(e) => setAsOf(e.target.value)} InputLabelProps={{ shrink: true }} />
        {data ? <Chip color="primary" label={`الإجمالي: ${money(data.total)} $`} /> : null}
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

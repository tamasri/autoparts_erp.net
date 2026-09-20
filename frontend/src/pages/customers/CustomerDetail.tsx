import { useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { Alert, Box, Button, Card, CardContent, CircularProgress, Stack, Typography } from '@mui/material';
import { customersApi } from '../../api/endpoints/customers';
import { unwrapNode } from '../../api/apiData';
import { extractApiError } from '../../lib/toast';
import PageHeader from '../../components/ui/PageHeader';
import ExportMenu from '../../components/ui/ExportMenu';
import DocumentDialog from '../../components/ui/DocumentDialog';
import StatementTable, { statementDocument, withRunning, type StatementLine } from '../../components/accounts/StatementTable';

type Customer = { id: string; code?: string; name?: string; type?: string; city?: string; phone?: string; creditLimitSyp?: number; creditLimitUsd?: number; paymentTermsDays?: number };
type Tx = { id: string; type: string; date: string; reference?: string; debitSyp: number; creditSyp: number; debitUsd: number; creditUsd: number; balanceSyp: number; balanceUsd: number };
type Statement = { totalInvoicedSyp: number; totalInvoicedUsd: number; totalPaidSyp: number; totalPaidUsd: number; outstandingSyp: number; outstandingUsd: number; transactions: Tx[] };

const money = (v?: number): string => Number(v ?? 0).toLocaleString('en-US', { maximumFractionDigits: 2 });

function Stat({ label, value, tone }: { label: string; value: string; tone?: 'error' | 'success' | 'primary' }): JSX.Element {
  return (
    <Card variant="outlined" sx={{ borderRadius: 3, flex: 1, minWidth: 180, borderInlineStart: 4, borderInlineStartColor: tone ? `${tone}.main` : 'divider' }}>
      <CardContent>
        <Typography variant="caption" color="text.secondary">{label}</Typography>
        <Typography variant="h5" fontWeight={800} color={tone ? `${tone}.main` : 'text.primary'}>{value}</Typography>
      </CardContent>
    </Card>
  );
}

export default function CustomerDetail(): JSX.Element {
  const { id } = useParams();
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [statement, setStatement] = useState<Statement | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [viewOpen, setViewOpen] = useState(false);

  useEffect(() => {
    if (!id) return;
    let live = true;
    setLoading(true); setError('');
    Promise.all([customersApi.getCustomerById(id), customersApi.getCustomerStatement(id)])
      .then(([c, s]) => { if (live) { setCustomer(unwrapNode<Customer>(c.data)); setStatement(unwrapNode<Statement>(s.data)); } })
      .catch((e: unknown) => { if (live) setError(extractApiError(e, 'تعذر تحميل بيانات العميل')); })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
  }, [id]);

  const lines: StatementLine[] = useMemo(
    () => withRunning((statement?.transactions ?? []).map((t) => ({ ...t, reference: t.reference }))),
    [statement],
  );
  const doc = useMemo(
    () => statementDocument(
      `كشف حساب ${customer?.name ?? ''}`, `${customer?.code ?? ''}`,
      [
        { label: 'العميل', value: customer?.name ?? '' }, { label: 'الكود', value: customer?.code ?? '' },
        { label: 'المستحق (ل.س)', value: money(statement?.outstandingSyp) }, { label: 'المستحق ($)', value: money(statement?.outstandingUsd) },
      ],
      lines,
    ),
    [customer, statement, lines],
  );

  if (loading) return <Box sx={{ display: 'grid', placeItems: 'center', minHeight: '50vh' }}><CircularProgress /></Box>;

  const outSyp = statement?.outstandingSyp ?? 0;
  const outUsd = statement?.outstandingUsd ?? 0;

  return (
    <Box>
      <PageHeader
        title={customer?.name ?? 'العميل'}
        subtitle={[customer?.code, customer?.phone, customer?.city].filter(Boolean).join(' · ')}
        crumbs={[{ label: 'الحسابات', to: '/accounts' }, { label: customer?.code ?? '' }]}
        actions={<><Button variant="outlined" size="small" onClick={() => setViewOpen(true)}>👁 عرض وطباعة</Button><ExportMenu build={async () => doc} /></>}
      />
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}

      <Stack direction="row" flexWrap="wrap" gap={2} sx={{ mb: 3 }}>
        <Stat label="المستحق (ل.س)" value={money(outSyp)} tone={outSyp > 0 ? 'error' : 'success'} />
        <Stat label="المستحق ($)" value={money(outUsd)} tone={outUsd > 0 ? 'error' : 'success'} />
        <Stat label="إجمالي الفواتير ($)" value={money(statement?.totalInvoicedUsd)} />
        <Stat label="إجمالي المقبوض ($)" value={money(statement?.totalPaidUsd)} tone="primary" />
        <Stat label="الحد الائتماني ($)" value={money(customer?.creditLimitUsd)} />
        <Stat label="شروط الدفع" value={`${customer?.paymentTermsDays ?? '-'} يوم`} />
      </Stack>

      <Typography variant="h6" fontWeight={700} sx={{ mb: 1 }}>كشف الحساب</Typography>
      <StatementTable
        lines={lines}
        linkFor={(l) => (['INVOICE', 'VOIDED', 'RETURN', 'CREDIT_NOTE'].includes(l.type) ? `/invoices/${l.id}` : null)}
      />
      <DocumentDialog open={viewOpen} onClose={() => setViewOpen(false)} document={doc} />
    </Box>
  );
}

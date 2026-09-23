/**
 * Account statement for an account (party). Only an account that is BOTH a customer and a vendor gets the combined statement
 * (receivable side, payable side, net position); a customer-only account gets its customer statement, a vendor-only account its
 * vendor statement.
 */
import { useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { Alert, Box, Button, Card, CardContent, CircularProgress, Stack, Typography } from '@mui/material';
import { partiesApi } from '../../api/endpoints/parties';
import { unwrapNode } from '../../api/apiData';
import { extractApiError } from '../../lib/toast';
import PageHeader from '../../components/ui/PageHeader';
import ExportMenu from '../../components/ui/ExportMenu';
import DocumentDialog from '../../components/ui/DocumentDialog';
import StatementTable, { statementDocument, withRunning, type StatementLine } from '../../components/accounts/StatementTable';
import Money from '../../components/ui/Money';

type Party = { id: string; code?: string; displayName?: string; displayNameAr?: string; hasCombinedStatement: boolean; showArTab: boolean; showApTab: boolean };
type Line = { date: string; entryType: string; referenceNumber: string; description: string; debitSyp: number; creditSyp: number; debitUsd: number; creditUsd: number };
type Balance = { outstandingSyp: number; outstandingUsd: number };
type Combined = { arLines: Line[]; apLines: Line[]; arBalance: Balance; apBalance: Balance; netPosition: Balance };
type SupplierStatement = { outstandingUsd: number; transactions: ArStatement['transactions'] };
type ArStatement = { transactions: Array<{ id: string; type: string; date: string; reference?: string; debitSyp: number; creditSyp: number; debitUsd: number; creditUsd: number; balanceSyp: number; balanceUsd: number }> };

const toLines = (rows: Line[]): StatementLine[] => withRunning(rows.map((r) => ({ date: r.date, type: r.entryType, reference: r.referenceNumber, description: r.description, debitSyp: r.debitSyp, creditSyp: r.creditSyp, debitUsd: r.debitUsd, creditUsd: r.creditUsd })));

function Stat({ label, syp, usd }: { label: string; syp: number; usd: number }): JSX.Element {
  return (
    <Card variant="outlined" sx={{ borderRadius: 3, flex: 1, minWidth: 220 }}>
      <CardContent>
        <Typography variant="caption" color="text.secondary">{label}</Typography>
        <Box sx={{ mt: 0.5 }}><Money usd={usd} syp={syp} variant="h6" fontWeight={800} /></Box>
      </CardContent>
    </Card>
  );
}

export default function CombinedStatement(): JSX.Element {
  const { id } = useParams();
  const [party, setParty] = useState<Party | null>(null);
  const [combined, setCombined] = useState<Combined | null>(null);
  const [ar, setAr] = useState<StatementLine[]>([]);
  const [supplier, setSupplier] = useState<StatementLine[]>([]);
  const [owedUsd, setOwedUsd] = useState(0);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [viewOpen, setViewOpen] = useState(false);

  useEffect(() => {
    if (!id) return;
    let live = true;
    (async () => {
      setLoading(true); setError('');
      try {
        const p = unwrapNode<Party>((await partiesApi.getPartyById(id)).data);
        if (!live || !p) return;
        setParty(p);
        if (p.hasCombinedStatement) {
          setCombined(unwrapNode<Combined>((await partiesApi.getCombinedStatement(id)).data));
        } else if (!p.showArTab && p.showApTab) {
          const s = unwrapNode<SupplierStatement>((await partiesApi.getApStatement(id)).data);
          setSupplier(s?.transactions.map((t) => ({ ...t })) ?? []);
          setOwedUsd(s?.outstandingUsd ?? 0);
        } else if (p.showArTab) {
          const s = unwrapNode<ArStatement>((await partiesApi.getArStatement(id)).data);
          setAr(withRunning((s?.transactions ?? []).map((t) => ({ ...t }))));
        }
      } catch (e: unknown) {
        if (live) setError(extractApiError(e, 'تعذر تحميل كشف الحساب'));
      } finally {
        if (live) setLoading(false);
      }
    })();
    return () => { live = false; };
  }, [id]);

  const arLines = useMemo(() => (combined ? toLines(combined.arLines) : ar), [combined, ar]);
  const supplierLines = useMemo(() => withRunning(supplier), [supplier]);
  const apLines = useMemo(() => (combined ? toLines(combined.apLines) : []), [combined]);
  const name = party?.displayNameAr || party?.displayName || '';
  const doc = useMemo(() => {
    const isSupplierOnly = party !== null && !party.showArTab && !party.hasCombinedStatement;
    const d = statementDocument(`كشف حساب ${name}`, party?.hasCombinedStatement ? 'كشف مدمج (زبون + مورد)' : isSupplierOnly ? 'كشف حساب المورد' : 'كشف حساب الزبون', [{ label: 'الحساب', value: name }, { label: 'الكود', value: party?.code ?? '' }], isSupplierOnly ? supplierLines : arLines, party?.hasCombinedStatement ? 'جانب الزبون (مدين علينا)' : 'كشف الحساب');
    if (combined) d.tables.push(...statementDocument('', '', [], apLines, 'جانب المورد (دائن لنا)').tables);
    return d;
  }, [name, party, combined, arLines, apLines, supplierLines]);

  if (loading) return <Box sx={{ display: 'grid', placeItems: 'center', minHeight: '50vh' }}><CircularProgress /></Box>;

  const vendorOnly = party !== null && !party.showArTab && !party.hasCombinedStatement;

  return (
    <Box>
      <PageHeader
        title={name || 'كشف الحساب'}
        subtitle={party?.hasCombinedStatement ? 'كشف مدمج: الحساب زبون ومورد في آن واحد' : party?.code}
        crumbs={[{ label: 'الحسابات', to: '/accounts' }, { label: 'كشف الحساب' }]}
        actions={<><Button variant="outlined" size="small" onClick={() => setViewOpen(true)}>👁 عرض وطباعة</Button><ExportMenu build={async () => doc} /></>}
      />
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}

      {combined ? (
        <Stack direction="row" flexWrap="wrap" gap={2} sx={{ mb: 3 }}>
          <Stat label="مستحق علينا من الحساب (زبون)" syp={combined.arBalance.outstandingSyp} usd={combined.arBalance.outstandingUsd} />
          <Stat label="مستحق للحساب علينا (مورد)" syp={combined.apBalance.outstandingSyp} usd={combined.apBalance.outstandingUsd} />
          <Stat label="صافي المركز" syp={combined.netPosition.outstandingSyp} usd={combined.netPosition.outstandingUsd} />
        </Stack>
      ) : null}

      {vendorOnly ? (
        <>
          <Stack direction="row" flexWrap="wrap" gap={2} sx={{ mb: 3 }}>
            <Card variant="outlined" sx={{ borderRadius: 3, flex: 1, minWidth: 220 }}>
              <CardContent>
                <Typography variant="caption" color="text.secondary">المستحق للمورد علينا</Typography>
                <Box><Money usd={owedUsd} variant="h5" fontWeight={800} color={owedUsd > 0 ? 'error.main' : 'success.main'} /></Box>
              </CardContent>
            </Card>
          </Stack>
          <Typography variant="h6" fontWeight={700} sx={{ mb: 1 }}>كشف حساب المورد</Typography>
          <StatementTable lines={supplierLines} linkFor={(l) => (l.type === 'BILL' ? '/purchasing' : l.type === 'SUPPLIER_PAYMENT' ? '/purchasing?tab=payments' : null)} />
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>في كشف المورد: الرصيد الموجب = ما زلنا مدينين به للمورد.</Typography>
        </>
      ) : null}

      {!vendorOnly ? (
        <>
          <Typography variant="h6" fontWeight={700} sx={{ mb: 1 }}>{combined ? 'جانب الزبون' : 'كشف الحساب'}</Typography>
          <StatementTable lines={arLines} />
        </>
      ) : null}
      {combined ? (
        <>
          <Typography variant="h6" fontWeight={700} sx={{ mt: 3, mb: 1 }}>جانب المورد</Typography>
          <StatementTable lines={apLines} />
        </>
      ) : null}
      <DocumentDialog open={viewOpen} onClose={() => setViewOpen(false)} document={doc} />
    </Box>
  );
}

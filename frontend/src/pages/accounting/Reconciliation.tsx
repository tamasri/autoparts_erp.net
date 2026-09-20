/**
 * تسوية الحسابات — tick the ledger lines of a bank or cash account that appear on the statement, until the difference to the statement
 * balance is zero. Reconciled lines are remembered, so each line is cleared once; the latest reconciliation can be undone.
 */
import { useEffect, useMemo, useState } from 'react';
import {
  Alert, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, LinearProgress, Stack, TextField, Typography,
} from '@mui/material';
import { accountingApi, type ReconcileCandidate, type ReconcileCandidates, type Reconciliation, type ReconciliationDetail } from '../../api/endpoints/accounting';
import { unwrapList, unwrapNode } from '../../api/apiData';
import { ACCOUNTING, useCan } from '../../hooks/useCan';
import { useChartAccounts } from '../../hooks/useChartAccounts';
import { useLoad } from '../../hooks/useLoad';
import { useConfirm } from '../../hooks/useConfirm';
import { extractApiError, toast } from '../../lib/toast';
import { money, moneyOrBlank, round4, today } from '../../lib/money';
import PageHeader from '../../components/ui/PageHeader';
import RoutedTabs from '../../components/ui/RoutedTabs';
import DataTable, { type Column } from '../../components/ui/DataTable';
import AccountPicker from '../../features/accounting/AccountPicker';
import { VOUCHER_LABEL } from '../../features/accounting/labels';

const plain = (name: string): string => name.replace(/ - [A-Z0-9]{1,8}$/, '');
const parse = (text: string): number => (text.trim() === '' ? NaN : Number(text.replace(/,/g, '')));

function NewReconciliation(): JSX.Element {
  const canReconcile = useCan(ACCOUNTING.reconcile);
  const { ledgers, loading: chartLoading } = useChartAccounts();
  const [account, setAccount] = useState<string | null>(null);
  const [date, setDate] = useState(today());
  const [balanceText, setBalanceText] = useState('');
  const [notes, setNotes] = useState('');
  const [ticked, setTicked] = useState<Set<string>>(new Set());
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const { data, loading, error: loadError, reload } = useLoad<ReconcileCandidates | null>(
    async () => unwrapNode<ReconcileCandidates>((await accountingApi.reconcileCandidates(account!, date)).data), [account, date], 'تعذر تحميل حركات الحساب', Boolean(account));
  useEffect(() => { setTicked(new Set()); setError(''); }, [account, date]);

  const sign = data?.debitNormal === false ? -1 : 1;
  const clearedNow = useMemo(() => round4(sign * (data?.rows ?? []).filter((r) => ticked.has(r.glName)).reduce((s, r) => s + r.debit - r.credit, 0)), [data, ticked, sign]);
  const statement = parse(balanceText);
  const difference = data && Number.isFinite(statement) ? round4(statement - (data.reconciledBalance + clearedNow)) : NaN;
  const balanced = Number.isFinite(difference) && Math.abs(difference) < 0.005 && ticked.size > 0;

  const toggle = (name: string): void => setTicked((s) => { const n = new Set(s); if (n.has(name)) n.delete(name); else n.add(name); return n; });

  async function save(): Promise<void> {
    if (!account) return;
    setSaving(true); setError('');
    try {
      await accountingApi.completeReconciliation({ account, statementDate: date, statementBalance: statement, glEntryNames: [...ticked], notes: notes.trim() || null });
      toast.success('تمت التسوية'); setTicked(new Set()); setBalanceText(''); setNotes(''); reload();
    } catch (e: unknown) { setError(extractApiError(e, 'تعذر حفظ التسوية')); }
    finally { setSaving(false); }
  }

  const columns: Column<ReconcileCandidate>[] = [
    { header: '', width: 48, render: (r) => <Checkbox size="small" checked={ticked.has(r.glName)} disabled={!canReconcile} onChange={() => toggle(r.glName)} /> },
    { header: 'التاريخ', nowrap: true, render: (r) => r.postingDate },
    { header: 'المستند', render: (r) => VOUCHER_LABEL[r.voucherType ?? ''] ?? r.voucherType ?? '—' },
    { header: 'الرقم', render: (r) => <span style={{ fontFamily: 'monospace' }}>{r.voucherNo ?? '—'}</span> },
    { header: 'الطرف / البيان', render: (r) => r.party ?? r.remarks ?? '' },
    { header: 'مدين', numeric: true, render: (r) => moneyOrBlank(r.debit) },
    { header: 'دائن', numeric: true, render: (r) => moneyOrBlank(r.credit) },
  ];

  return (
    <Stack spacing={2}>
      <Stack direction="row" gap={1.5} alignItems="center" flexWrap="wrap">
        <div style={{ width: 300 }}><AccountPicker accounts={ledgers} value={account} loading={chartLoading} onChange={(a) => setAccount(a?.name ?? null)} label="الحساب (مصرف / صندوق)" /></div>
        <TextField size="small" type="date" label="تاريخ الكشف" value={date} onChange={(e) => setDate(e.target.value)} InputLabelProps={{ shrink: true }} />
        <TextField size="small" label="رصيد الكشف الختامي" value={balanceText} onChange={(e) => setBalanceText(e.target.value)} inputProps={{ inputMode: 'decimal', dir: 'ltr' }} sx={{ width: 180 }} />
      </Stack>
      {loading ? <LinearProgress /> : null}
      {loadError ? <Alert severity="error">{loadError}</Alert> : null}
      {error ? <Alert severity="error">{error}</Alert> : null}
      {!account ? <Alert severity="info">اختر الحساب وتاريخ الكشف وأدخل رصيده الختامي، ثم علّم الحركات الظاهرة في الكشف.</Alert> : null}
      {data ? (
        <>
          <Stack direction="row" gap={1} flexWrap="wrap" alignItems="center">
            <Chip label={`الرصيد حسب الدفاتر: ${money(data.bookBalance)}`} />
            <Chip label={`المُسوّى سابقاً: ${money(data.reconciledBalance)}`} />
            <Chip label={`المعلَّم الآن: ${money(clearedNow)} (${ticked.size} حركة)`} color="primary" variant="outlined" />
            <Chip
              color={!Number.isFinite(difference) ? 'default' : Math.abs(difference) < 0.005 ? 'success' : 'warning'}
              label={Number.isFinite(difference) ? `الفرق عن الكشف: ${money(difference)}` : 'أدخل رصيد الكشف لحساب الفرق'}
            />
          </Stack>
          {data.truncated ? <Alert severity="warning">للحساب أكثر من 5000 حركة؛ عُرضت الأقدم فقط.</Alert> : null}
          <DataTable columns={columns} rows={data.rows} getKey={(r) => r.glName} empty="لا توجد حركات غير مُسوّاة حتى هذا التاريخ" />
          <Stack direction="row" gap={1.5} alignItems="center" flexWrap="wrap">
            <Button size="small" onClick={() => setTicked(new Set(data.rows.map((r) => r.glName)))} disabled={!canReconcile}>تحديد الكل</Button>
            <Button size="small" onClick={() => setTicked(new Set())}>إلغاء التحديد</Button>
            <TextField size="small" label="ملاحظات" value={notes} onChange={(e) => setNotes(e.target.value)} sx={{ flex: 1, minWidth: 200 }} />
            <Button variant="contained" disabled={!canReconcile || !balanced || saving} onClick={() => void save()}>إتمام التسوية</Button>
          </Stack>
          {!canReconcile ? <Typography variant="caption" color="text.secondary">لا تملك صلاحية إجراء التسوية.</Typography> : null}
        </>
      ) : null}
    </Stack>
  );
}

function History(): JSX.Element {
  const canReconcile = useCan(ACCOUNTING.reconcile);
  const { ledgers } = useChartAccounts();
  const [account, setAccount] = useState<string | null>(null);
  const [detail, setDetail] = useState<ReconciliationDetail | null>(null);
  const { confirm, dialog: confirmDialog } = useConfirm();
  const { data, loading, error, reload } = useLoad<Reconciliation[]>(async () => unwrapList<Reconciliation>((await accountingApi.reconciliations(account ?? undefined)).data), [account], 'تعذر تحميل سجل التسويات');

  async function open(id: string): Promise<void> {
    try { setDetail(unwrapNode<ReconciliationDetail>((await accountingApi.reconciliation(id)).data)); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر تحميل التسوية')); }
  }

  async function undo(r: Reconciliation): Promise<void> {
    if (!(await confirm(`التراجع عن تسوية ${plain(r.account)} بتاريخ ${r.statementDate}؟ ستعود حركاتها غير مُسوّاة.`, { confirmLabel: 'تراجع' }))) return;
    try { await accountingApi.undoReconciliation(r.id); toast.success('تم التراجع عن التسوية'); reload(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر التراجع')); }
  }

  const columns: Column<Reconciliation>[] = [
    { header: 'الحساب', render: (r) => <strong>{plain(r.account)}</strong> },
    { header: 'تاريخ الكشف', nowrap: true, render: (r) => r.statementDate },
    { header: 'رصيد الكشف', numeric: true, render: (r) => money(r.statementBalance) },
    { header: 'الحركات', numeric: true, render: (r) => r.itemCount },
    { header: 'أُجريت بواسطة', render: (r) => r.completedBy ?? '—' },
    { header: 'وقت التسوية', nowrap: true, render: (r) => new Date(r.completedAt).toLocaleString('ar') },
    { header: '', nowrap: true, render: (r) => (<Stack direction="row" gap={0.5}><Button size="small" onClick={() => void open(r.id)}>الحركات</Button>{canReconcile ? <Button size="small" color="error" onClick={() => void undo(r)}>تراجع</Button> : null}</Stack>) },
  ];

  return (
    <Stack spacing={2}>
      <div style={{ width: 300 }}><AccountPicker accounts={ledgers} value={account} onChange={(a) => setAccount(a?.name ?? null)} label="تصفية بالحساب" /></div>
      {loading ? <LinearProgress /> : null}
      {error ? <Alert severity="error">{error}</Alert> : null}
      <DataTable columns={columns} rows={data ?? []} getKey={(r) => r.id} loading={loading} empty="لا توجد تسويات بعد" />
      {confirmDialog}
      <Dialog open={Boolean(detail)} onClose={() => setDetail(null)} fullWidth maxWidth="sm">
        <DialogTitle>{detail ? `حركات تسوية ${plain(detail.reconciliation.account)} — ${detail.reconciliation.statementDate}` : ''}</DialogTitle>
        <DialogContent dividers>
          <DataTable
            columns={[
              { header: 'التاريخ', render: (i: ReconciliationDetail['items'][number]) => i.postingDate },
              { header: 'المستند', render: (i) => VOUCHER_LABEL[i.voucherType ?? ''] ?? i.voucherType ?? '—' },
              { header: 'الرقم', render: (i) => <span style={{ fontFamily: 'monospace' }}>{i.voucherNo ?? '—'}</span> },
              { header: 'المبلغ', numeric: true, render: (i) => money(i.signedAmount) },
            ]}
            rows={detail?.items ?? []} getKey={(i) => i.glName}
          />
        </DialogContent>
        <DialogActions><Button onClick={() => setDetail(null)}>إغلاق</Button></DialogActions>
      </Dialog>
    </Stack>
  );
}

export default function Reconciliation(): JSX.Element {
  return (
    <>
      <PageHeader title="تسوية الحسابات" subtitle="طابِق حركات الصندوق أو المصرف في الدفتر مع كشف الحساب" />
      <RoutedTabs tabs={[{ key: 'new', label: 'تسوية جديدة', content: <NewReconciliation /> }, { key: 'history', label: 'سجل التسويات', content: <History /> }]} />
    </>
  );
}

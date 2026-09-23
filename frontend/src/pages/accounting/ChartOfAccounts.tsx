/**
 * شجرة الحسابات — the chart of accounts in ERPNext (groups and ledger accounts) with live balances. Accounts are created, renamed,
 * retyped and disabled here (saved straight into ERPNext), loaded in bulk from Excel/CSV, and exported. Nothing is kept twice.
 */
import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { SimpleTreeView } from '@mui/x-tree-view/SimpleTreeView';
import { TreeItem } from '@mui/x-tree-view/TreeItem';
import { accountingApi, type Account, type AccountImportResult, type AccountMapping } from '../../api/endpoints/accounting';
import { unwrapList, unwrapNode } from '../../api/apiData';
import { ACCOUNTING, useCan } from '../../hooks/useCan';
import { useConfirm } from '../../hooks/useConfirm';
import { extractApiError, toast } from '../../lib/toast';
import { money } from '../../lib/money';
import { num, type ExportDocument } from '../../lib/exportClient';
import PageHeader from '../../components/ui/PageHeader';
import ExportMenu from '../../components/ui/ExportMenu';
import ImportDialog, { type ImportSummary } from '../../components/ui/ImportDialog';
import AccountDialog, { type AccountDialogMode } from '../../features/accounting/AccountDialog';
import { ROOT_COLOR, ROOT_LABEL, accountTypeLabel } from '../../features/accounting/labels';
import Money from '../../components/ui/Money';

type Node = Account & { children: Node[]; depth: number };

const PURPOSE_LABEL: Record<string, string> = { RECEIVABLE: 'ذمم الزبائن', PAYABLE: 'ذمم الموردين', CASH: 'الصندوق (نقد)', BANK: 'المصرف', INCOME: 'الإيرادات', COGS: 'تكلفة البضاعة المباعة', INVENTORY: 'المخزون' };
const IMPORT_STATUS = { CREATE: 'ok', CREATED: 'ok', EXISTS: 'skipped', ERROR: 'error' } as const;

function buildTree(flat: Account[]): Node[] {
  const byName = new Map<string, Node>(flat.map((a) => [a.name, { ...a, children: [], depth: 0 }]));
  const roots: Node[] = [];
  for (const n of byName.values()) {
    const parent = n.parentAccount ? byName.get(n.parentAccount) : undefined;
    if (parent) parent.children.push(n); else roots.push(n);
  }
  const setDepth = (n: Node, d: number): void => { n.depth = d; n.children.forEach((c) => setDepth(c, d + 1)); };
  roots.forEach((r) => setDepth(r, 0));
  return roots;
}

const flatten = (nodes: Node[]): Node[] => nodes.flatMap((n) => [n, ...flatten(n.children)]);

function renderNodes(nodes: Node[], onPick: (n: Node) => void): JSX.Element[] {
  return nodes.map((n) => (
    <TreeItem
      key={n.name} itemId={n.name} onClick={(e) => { e.stopPropagation(); onPick(n); }}
      label={(
        <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ py: 0.4, pr: 1 }}>
          <Typography variant="body2" fontWeight={n.isGroup ? 700 : 400}>{n.accountName}</Typography>
          {n.balance ? ((n.currency ?? 'USD') === 'USD' ? <Money usd={n.balance} inline /> : <Typography variant="body2" sx={{ direction: 'ltr' }}>{money(n.balance)} {n.currency}</Typography>) : <Typography variant="body2" color="text.disabled">—</Typography>}
        </Stack>
      )}
    >
      {n.children.length > 0 ? renderNodes(n.children, onPick) : null}
    </TreeItem>
  ));
}

async function uploadAccounts(file: File, dryRun: boolean): Promise<ImportSummary> {
  const r = unwrapNode<AccountImportResult>((await accountingApi.importAccounts(file, dryRun)).data) as AccountImportResult;
  return { dryRun: r.dryRun, total: r.total, created: r.created, skipped: r.existing, failed: r.failed, rows: r.rows.map((x) => ({ rowNumber: x.rowNumber, label: x.accountName, status: IMPORT_STATUS[x.status], message: x.message })) };
}

export default function ChartOfAccounts(): JSX.Element {
  const canManage = useCan(ACCOUNTING.manageAccounts);
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [mapping, setMapping] = useState<AccountMapping[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<string | null>(null);
  const [dialog, setDialog] = useState<AccountDialogMode | null>(null);
  const [importing, setImporting] = useState(false);
  const { confirm, dialog: confirmDialog } = useConfirm();

  const load = useCallback(async (): Promise<void> => {
    setLoading(true); setError('');
    try {
      const map = accountingApi.mapping().catch(() => null);
      let tree;
      try {
        tree = await accountingApi.accounts(true);
      } catch (e: unknown) {
        // The balances come from a separate ledger query; if only that fails, still show the chart (it can be maintained
        // without balances) and say why the balance column is empty.
        tree = await accountingApi.accounts(false);
        setError(`تعذّرت قراءة الأرصدة، فالشجرة معروضة دونها — ${extractApiError(e, 'خطأ من ERPNext')}`);
      }
      setAccounts(unwrapList<Account>(tree.data));
      const m = await map;
      setMapping(m ? unwrapList<AccountMapping>(m.data) : []);
    } catch (e: unknown) { setError(extractApiError(e, 'تعذر قراءة شجرة الحسابات من ERPNext')); }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  const tree = useMemo(() => buildTree(accounts), [accounts]);
  const all = useMemo(() => flatten(tree), [tree]);
  const current = all.find((n) => n.name === selected) ?? null;
  const visible = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return tree;
    const keep = (n: Node): Node | null => {
      const kids = n.children.map(keep).filter((c): c is Node => c !== null);
      return n.accountName.toLowerCase().includes(q) || n.name.toLowerCase().includes(q) || kids.length > 0 ? { ...n, children: kids } : null;
    };
    return tree.map(keep).filter((n): n is Node => n !== null);
  }, [tree, search]);

  async function disable(account: Account): Promise<void> {
    if (!(await confirm(`تعطيل الحساب «${account.accountName}»؟ يختفي من الشجرة وتبقى قيوده السابقة في الدفتر.`, { confirmLabel: 'تعطيل' }))) return;
    try { await accountingApi.updateAccount(account.name, { disabled: true }); toast.success('عُطّل الحساب'); setSelected(null); await load(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر تعطيل الحساب')); }
  }

  const buildExport = async (): Promise<ExportDocument> => ({
    title: 'شجرة الحسابات', subtitle: 'من ERPNext', fileName: 'chart-of-accounts', fields: [],
    tables: [{
      columns: ['اسم الحساب', 'الحساب الأب', 'مجموعة؟', 'نوع الحساب', 'الفئة', 'العملة', 'الرصيد'],
      rows: all.map((n) => [`${'    '.repeat(n.depth)}${n.accountName}`, n.parentAccount ?? '', n.isGroup ? '1' : '0', n.accountType ?? '', ROOT_LABEL[n.rootType ?? ''] ?? n.rootType ?? '', n.currency ?? '', num(n.balance)]),
      numericColumns: [6],
    }],
  });

  const mappedTo = new Map<string, string[]>();
  for (const m of mapping) if (m.account) mappedTo.set(m.account, [...(mappedTo.get(m.account) ?? []), PURPOSE_LABEL[m.purpose] ?? m.purpose]);

  return (
    <Box>
      <PageHeader
        title="شجرة الحسابات" subtitle="المجموعات وحسابات الحركة كما في دفتر الأستاذ (ERPNext) مع أرصدتها الحية"
        actions={(
          <>
            <Button variant="outlined" size="small" onClick={() => void load()}>↻ تحديث</Button>
            <ExportMenu build={buildExport} disabled={loading || accounts.length === 0} />
            {canManage ? <Button variant="outlined" size="small" onClick={() => setImporting(true)}>⬆ استيراد Excel / CSV</Button> : null}
            {canManage ? <Button variant="contained" size="small" onClick={() => setDialog({ kind: 'create', parent: current?.isGroup ? current.name : current?.parentAccount ?? null })}>＋ حساب جديد</Button> : null}
          </>
        )}
      />
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '3fr 2fr' }, gap: 3, alignItems: 'start' }}>
        <Card variant="outlined" sx={{ borderRadius: 3 }}>
          <CardContent>
            <TextField size="small" fullWidth placeholder="بحث في الحسابات..." value={search} onChange={(e) => setSearch(e.target.value)} sx={{ mb: 2 }} />
            {loading ? <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}><CircularProgress /></Box> : accounts.length === 0 ? (
              <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>لا توجد حسابات (تحقق من تفعيل ERPNext)</Typography>
            ) : (
              <SimpleTreeView key={search ? 'search' : 'all'} defaultExpandedItems={search.trim() ? flatten(visible).map((n) => n.name) : all.filter((n) => n.isGroup).map((n) => n.name)} expansionTrigger="iconContainer" selectedItems={selected}>
                {renderNodes(visible, (n) => setSelected(n.name))}
              </SimpleTreeView>
            )}
          </CardContent>
        </Card>

        <Stack spacing={3}>
          <Card variant="outlined" sx={{ borderRadius: 3 }}>
            <CardContent>
              <Typography variant="h6" fontWeight={700} sx={{ mb: 1 }}>الحساب المحدد</Typography>
              {current ? (
                <Stack spacing={1}>
                  <Typography fontWeight={800}>{current.accountName}</Typography>
                  <Typography variant="caption" color="text.secondary" sx={{ direction: 'ltr', textAlign: 'right' }}>{current.name}</Typography>
                  <Stack direction="row" gap={1} flexWrap="wrap">
                    {current.rootType ? <Chip size="small" color={ROOT_COLOR[current.rootType] ?? 'default'} label={ROOT_LABEL[current.rootType] ?? current.rootType} /> : null}
                    <Chip size="small" variant="outlined" label={current.isGroup ? 'مجموعة' : 'حساب حركة'} />
                    {current.accountType ? <Chip size="small" variant="outlined" label={accountTypeLabel(current.accountType)} /> : null}
                    {current.currency ? <Chip size="small" variant="outlined" label={current.currency} /> : null}
                  </Stack>
                  {current.balance !== null ? ((current.currency ?? 'USD') === 'USD' ? <Money usd={current.balance} variant="h5" fontWeight={800} /> : <Typography variant="h5" fontWeight={800} sx={{ direction: 'ltr', textAlign: 'right' }}>{money(current.balance)} {current.currency}</Typography>) : null}
                  {mappedTo.has(current.name) ? <Alert severity="info">يستخدمه النظام: {mappedTo.get(current.name)!.join('، ')}</Alert> : null}
                  <Stack direction="row" gap={1} flexWrap="wrap" sx={{ pt: 1 }}>
                    {!current.isGroup ? <Button size="small" variant="contained" component={RouterLink} to={`/accounting/reports?tab=ledger&account=${encodeURIComponent(current.name)}`}>كشف الحساب</Button> : null}
                    {canManage && current.isGroup ? <Button size="small" variant="outlined" onClick={() => setDialog({ kind: 'create', parent: current.name })}>＋ حساب فرعي</Button> : null}
                    {canManage ? <Button size="small" variant="outlined" onClick={() => setDialog({ kind: 'edit', account: current })}>تعديل</Button> : null}
                    {canManage && !mappedTo.has(current.name) && current.parentAccount ? <Button size="small" color="error" onClick={() => void disable(current)}>تعطيل</Button> : null}
                  </Stack>
                </Stack>
              ) : <Typography color="text.secondary">اختر حساباً من الشجرة.</Typography>}
            </CardContent>
          </Card>

          <Card variant="outlined" sx={{ borderRadius: 3 }}>
            <CardContent>
              <Typography variant="h6" fontWeight={700}>ربط النظام بالحسابات</Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>أي حساب في ERPNext يُرحَّل إليه كل حدث في النظام (لا يُغيَّر من هنا حتى لا ينكسر الترحيل).</Typography>
              <Table size="small">
                <TableHead><TableRow><TableCell>الحدث</TableCell><TableCell>حساب ERPNext</TableCell></TableRow></TableHead>
                <TableBody>
                  {mapping.length === 0 ? <TableRow><TableCell colSpan={2} align="center" sx={{ color: 'text.secondary' }}>—</TableCell></TableRow> : mapping.map((m) => (
                    <TableRow key={m.purpose} hover>
                      <TableCell><Typography variant="body2" fontWeight={600}>{PURPOSE_LABEL[m.purpose] ?? m.purpose}</Typography><Typography variant="caption" color="text.secondary">{m.description}</Typography></TableCell>
                      <TableCell>{m.account ? <Chip size="small" label={m.account} color="success" variant="outlined" onClick={() => setSelected(m.account)} /> : <Chip size="small" label="غير معرّف" color="warning" />}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </Stack>
      </Box>

      <AccountDialog open={dialog !== null} mode={dialog} accounts={accounts} onClose={() => setDialog(null)} onSaved={(name) => { setSelected(name); void load(); }} />
      <ImportDialog
        open={importing} onClose={() => setImporting(false)} onImported={() => void load()} title="استيراد شجرة حسابات من Excel / CSV" noun="حساب" labelHeader="الحساب" templateFileName="accounts-template"
        intro="حمّل القالب وعبّئ الحسابات (الاسم، الحساب الأب، مجموعة أم حساب حركة، النوع). ضع الأب قبل أبنائه. يُفحص الملف أولاً دون كتابة، والحسابات الموجودة تُتجاوز، ويمكنك استيراد ما صدّرته من هذه الشجرة نفسها."
        downloadTemplate={async (format) => (await accountingApi.accountImportTemplate(format)).data as Blob} upload={uploadAccounts}
      />
      {confirmDialog}
    </Box>
  );
}

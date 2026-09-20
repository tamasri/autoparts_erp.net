/**
 * Chart of accounts, read live from ERPNext (the ledger's system of record) and shown inside our own UI, together with the
 * mapping of application events to ERPNext accounts. Read-only: accounts are managed in one place (ERPNext) and never edited here.
 */
import { useCallback, useEffect, useMemo, useState } from 'react';
import { Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import { SimpleTreeView } from '@mui/x-tree-view/SimpleTreeView';
import { TreeItem } from '@mui/x-tree-view/TreeItem';
import { erpnextBrowseApi, type ErpAccount, type ErpMapping } from '../../api/endpoints/erpnextBrowse';
import { unwrapList } from '../../api/apiData';
import { extractApiError } from '../../lib/toast';
import { num, type ExportDocument } from '../../lib/exportClient';
import PageHeader from '../../components/ui/PageHeader';
import ExportMenu from '../../components/ui/ExportMenu';

type Node = ErpAccount & { children: Node[]; depth: number };

const ROOT_LABEL: Record<string, string> = { Asset: 'أصول', Liability: 'التزامات', Equity: 'حقوق ملكية', Income: 'إيرادات', Expense: 'مصروفات' };
const ROOT_COLOR: Record<string, 'primary' | 'warning' | 'secondary' | 'success' | 'error'> = { Asset: 'primary', Liability: 'warning', Equity: 'secondary', Income: 'success', Expense: 'error' };
const PURPOSE_LABEL: Record<string, string> = { RECEIVABLE: 'ذمم الزبائن', PAYABLE: 'ذمم الموردين', CASH: 'الصندوق (نقد)', BANK: 'المصرف', INCOME: 'الإيرادات', COGS: 'تكلفة البضاعة المباعة', INVENTORY: 'المخزون' };

const money = (v: number | null | undefined): string => (v === null || v === undefined ? '' : v.toLocaleString('en-US', { maximumFractionDigits: 2 }));

function buildTree(flat: ErpAccount[]): Node[] {
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

function flatten(nodes: Node[]): Node[] {
  return nodes.flatMap((n) => [n, ...flatten(n.children)]);
}

function renderNodes(nodes: Node[], showBalances: boolean, onPick: (n: Node) => void): JSX.Element[] {
  return nodes.map((n) => (
    <TreeItem
      key={n.name}
      itemId={n.name}
      onClick={(e) => { e.stopPropagation(); onPick(n); }}
      label={(
        <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ py: 0.4, pr: 1 }}>
          <Typography variant="body2" fontWeight={n.isGroup ? 700 : 400}>{n.accountName}</Typography>
          {showBalances ? <Typography variant="body2" color={n.balance ? 'text.primary' : 'text.disabled'} sx={{ direction: 'ltr', fontVariantNumeric: 'tabular-nums' }}>{money(n.balance) || '—'}</Typography> : null}
        </Stack>
      )}
    >
      {n.children.length > 0 ? renderNodes(n.children, showBalances, onPick) : null}
    </TreeItem>
  ));
}

export default function ChartOfAccounts(): JSX.Element {
  const [accounts, setAccounts] = useState<ErpAccount[]>([]);
  const [mapping, setMapping] = useState<ErpMapping[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadingBalances, setLoadingBalances] = useState(false);
  const [hasBalances, setHasBalances] = useState(false);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<Node | null>(null);

  const load = useCallback(async (): Promise<void> => {
    setLoading(true); setError(''); setHasBalances(false);
    try {
      const [tree, map] = await Promise.all([erpnextBrowseApi.accounts(false), erpnextBrowseApi.mapping().catch(() => null)]);
      setAccounts(unwrapList<ErpAccount>(tree.data));
      setMapping(map ? unwrapList<ErpMapping>(map.data) : []);
    } catch (e: unknown) {
      setError(extractApiError(e, 'تعذر قراءة شجرة الحسابات من ERPNext'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function loadBalances(): Promise<void> {
    setLoadingBalances(true);
    try {
      const res = await erpnextBrowseApi.accounts(true);
      setAccounts(unwrapList<ErpAccount>(res.data));
      setHasBalances(true);
    } catch (e: unknown) {
      setError(extractApiError(e, 'تعذر قراءة الأرصدة'));
    } finally {
      setLoadingBalances(false);
    }
  }

  const tree = useMemo(() => buildTree(accounts), [accounts]);
  const visible = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return tree;
    const keep = (n: Node): Node | null => {
      const kids = n.children.map(keep).filter((c): c is Node => c !== null);
      return n.accountName.toLowerCase().includes(q) || n.name.toLowerCase().includes(q) || kids.length > 0 ? { ...n, children: kids } : null;
    };
    return tree.map(keep).filter((n): n is Node => n !== null);
  }, [tree, search]);
  const expandedIds = useMemo(() => (search.trim() ? flatten(visible).map((n) => n.name) : undefined), [visible, search]);

  const buildExport = async (): Promise<ExportDocument> => ({
    title: 'شجرة الحسابات', subtitle: 'من ERPNext', fileName: 'chart-of-accounts', fields: [],
    tables: [{
      columns: ['الحساب', 'الاسم', 'المستوى', 'النوع', 'الفئة', 'العملة', 'الرصيد'],
      rows: flatten(tree).map((n) => [n.name, `${'    '.repeat(n.depth)}${n.accountName}`, String(n.depth + 1), n.isGroup ? 'مجموعة' : 'حساب', ROOT_LABEL[n.rootType ?? ''] ?? n.rootType ?? '', n.currency ?? '', num(n.balance)]),
      numericColumns: [6],
    }],
  });

  const mapped = new Set(mapping.map((m) => m.account).filter(Boolean));

  return (
    <Box>
      <PageHeader
        title="شجرة الحسابات"
        subtitle="تُقرأ مباشرة من ERPNext — مصدر الحقيقة المحاسبي — للعرض والربط فقط"
        actions={(
          <>
            <Button variant="outlined" size="small" onClick={() => void load()}>↻ تحديث</Button>
            <Button variant="contained" size="small" disabled={loadingBalances || loading} onClick={() => void loadBalances()}>
              {loadingBalances ? 'جارٍ قراءة الأرصدة...' : hasBalances ? 'تحديث الأرصدة' : 'عرض الأرصدة'}
            </Button>
            <ExportMenu build={buildExport} disabled={loading || accounts.length === 0} />
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
              <SimpleTreeView key={search ? 'search' : 'all'} defaultExpandedItems={expandedIds ?? flatten(tree).filter((n) => n.isGroup).map((n) => n.name)} expansionTrigger="iconContainer">
                {renderNodes(visible, hasBalances, setSelected)}
              </SimpleTreeView>
            )}
          </CardContent>
        </Card>

        <Stack spacing={3}>
          <Card variant="outlined" sx={{ borderRadius: 3 }}>
            <CardContent>
              <Typography variant="h6" fontWeight={700} sx={{ mb: 1 }}>الحساب المحدد</Typography>
              {selected ? (
                <Stack spacing={0.8}>
                  <Typography fontWeight={800}>{selected.accountName}</Typography>
                  <Typography variant="caption" color="text.secondary" sx={{ direction: 'ltr', textAlign: 'right' }}>{selected.name}</Typography>
                  <Stack direction="row" gap={1} flexWrap="wrap">
                    {selected.rootType ? <Chip size="small" color={ROOT_COLOR[selected.rootType] ?? 'default'} label={ROOT_LABEL[selected.rootType] ?? selected.rootType} /> : null}
                    <Chip size="small" variant="outlined" label={selected.isGroup ? 'مجموعة' : 'حساب حركة'} />
                    {selected.accountType ? <Chip size="small" variant="outlined" label={selected.accountType} /> : null}
                    {selected.currency ? <Chip size="small" variant="outlined" label={selected.currency} /> : null}
                  </Stack>
                  {selected.balance !== null ? <Typography variant="h5" fontWeight={800}>{money(selected.balance)} {selected.currency}</Typography> : null}
                  {mapped.has(selected.name) ? <Alert severity="info" sx={{ mt: 1 }}>يستخدمه النظام: {mapping.filter((m) => m.account === selected.name).map((m) => PURPOSE_LABEL[m.purpose] ?? m.purpose).join('، ')}</Alert> : null}
                </Stack>
              ) : <Typography color="text.secondary">اختر حساباً من الشجرة.</Typography>}
            </CardContent>
          </Card>

          <Card variant="outlined" sx={{ borderRadius: 3 }}>
            <CardContent>
              <Typography variant="h6" fontWeight={700}>ربط النظام بالحسابات</Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>أي حساب في ERPNext يُرحَّل إليه كل حدث في النظام.</Typography>
              <Table size="small">
                <TableHead><TableRow><TableCell>الحدث</TableCell><TableCell>حساب ERPNext</TableCell></TableRow></TableHead>
                <TableBody>
                  {mapping.length === 0 ? <TableRow><TableCell colSpan={2} align="center" sx={{ color: 'text.secondary' }}>—</TableCell></TableRow> : mapping.map((m) => (
                    <TableRow key={m.purpose} hover>
                      <TableCell><Typography variant="body2" fontWeight={600}>{PURPOSE_LABEL[m.purpose] ?? m.purpose}</Typography><Typography variant="caption" color="text.secondary">{m.description}</Typography></TableCell>
                      <TableCell>{m.account ? <Chip size="small" label={m.account} color="success" variant="outlined" /> : <Chip size="small" label="غير معرّف" color="warning" />}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </Stack>
      </Box>
    </Box>
  );
}

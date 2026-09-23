/** Ledger statement (general ledger) of one account: every posting with a running balance, source documents and tags. */
import { useCallback, useEffect, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Button, Chip, LinearProgress, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { accountingApi, type LedgerRow, type LedgerStatement, type Tag } from '../../../api/endpoints/accounting';
import { unwrapList, unwrapNode } from '../../../api/apiData';
import { useCan, ACCOUNTING } from '../../../hooks/useCan';
import { useChartAccounts } from '../../../hooks/useChartAccounts';
import { useLoad } from '../../../hooks/useLoad';
import { today, yearStart } from '../../../lib/money';
import DataTable, { type Column } from '../../../components/ui/DataTable';
import ExportMenu from '../../../components/ui/ExportMenu';
import type { ExportDocument } from '../../../lib/exportClient';
import AccountPicker from '../AccountPicker';
import TagChips from '../TagChips';
import { ROOT_LABEL, VOUCHER_LABEL, sourceRoute } from '../labels';
import { ledgerDocument } from '../documents';
import Money from '../../../components/ui/Money';

/** A ledger export takes at most this many lines (fetched page by page); beyond it the file says how many were left out. */
const EXPORT_LIMIT = 20000;
const EXPORT_PAGE = 5000;

export default function LedgerReport({ initialAccount }: { initialAccount?: string | null }): JSX.Element {
  const canTag = useCan(ACCOUNTING.postEntries);
  const { ledgers, loading: chartLoading } = useChartAccounts();
  const [account, setAccount] = useState<string | null>(initialAccount ?? null);
  const [from, setFrom] = useState(yearStart());
  const [to, setTo] = useState(today());
  const [party, setParty] = useState('');
  const [partyApplied, setPartyApplied] = useState('');
  const [tagId, setTagId] = useState('');
  const [tags, setTags] = useState<Tag[]>([]);
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(100);

  useEffect(() => { const h = window.setTimeout(() => setPartyApplied(party.trim()), 400); return () => window.clearTimeout(h); }, [party]);
  useEffect(() => { setAccount(initialAccount ?? null); }, [initialAccount]);
  useEffect(() => { setPage(0); }, [account, from, to, partyApplied, tagId]);
  const loadTags = useCallback(() => { accountingApi.tags().then((r) => setTags(unwrapList<Tag>(r.data))).catch(() => undefined); }, []);
  useEffect(loadTags, [loadTags]);

  const { data, loading, error, reload } = useLoad<LedgerStatement | null>(
    async () => unwrapNode<LedgerStatement>((await accountingApi.ledger({ account: account!, from, to, party: partyApplied || undefined, tagId: tagId || undefined, page: page + 1, pageSize })).data),
    [account, from, to, partyApplied, tagId, page, pageSize], 'تعذر إعداد كشف الحساب', Boolean(account) && from <= to);

  /** Every page of the period, up to EXPORT_LIMIT lines, as one statement. */
  async function buildExport(): Promise<ExportDocument> {
    const base = { account: account!, from, to, party: partyApplied || undefined, tagId: tagId || undefined, pageSize: EXPORT_PAGE };
    const first = unwrapNode<LedgerStatement>((await accountingApi.ledger({ ...base, page: 1 })).data) as LedgerStatement;
    const rows = [...first.rows];
    for (let p = 2; rows.length < Math.min(first.totalCount, EXPORT_LIMIT); p++) {
      const next = (unwrapNode<LedgerStatement>((await accountingApi.ledger({ ...base, page: p })).data) as LedgerStatement).rows;
      if (next.length === 0) break; // the ledger shrank while exporting
      rows.push(...next);
    }

    const kept = rows.slice(0, EXPORT_LIMIT);
    return ledgerDocument({ ...first, rows: kept }, partyApplied, Math.max(0, first.totalCount - kept.length));
  }

  const columns: Column<LedgerRow>[] = [
    { header: 'التاريخ', nowrap: true, render: (r) => r.postingDate },
    { header: 'المستند', render: (r) => VOUCHER_LABEL[r.voucherType ?? ''] ?? r.voucherType ?? '—' },
    {
      header: 'الرقم', nowrap: true,
      render: (r) => {
        const to = sourceRoute(r.localEntityType, r.localEntityId);
        return to ? <Button size="small" component={RouterLink} to={to} sx={{ fontFamily: 'monospace' }}>{r.voucherNo}</Button> : <span style={{ fontFamily: 'monospace' }}>{r.voucherNo ?? '—'}</span>;
      },
    },
    { header: 'الطرف', render: (r) => r.party ?? '—' },
    { header: 'البيان', render: (r) => <Typography variant="body2" sx={{ maxWidth: 240 }} noWrap title={r.remarks ?? ''}>{r.remarks ?? ''}</Typography> },
    { header: 'مدين', numeric: true, render: (r) => (r.debit ? <Money usd={r.debit} /> : '') },
    { header: 'دائن', numeric: true, render: (r) => (r.credit ? <Money usd={r.credit} /> : '') },
    { header: 'الرصيد', numeric: true, render: (r) => <Money usd={r.balance} fontWeight={700} /> },
    {
      header: 'وسوم',
      render: (r) => r.voucherType && r.voucherNo
        ? <TagChips tags={r.tags} allTags={tags} canEdit={canTag} target={{ targetType: 'ERPNEXT', targetKey: `${r.voucherType}|${r.voucherNo}` }} onChanged={reload} /> : null,
    },
  ];

  return (
    <Stack spacing={2}>
      <Stack direction="row" gap={1.5} alignItems="center" flexWrap="wrap">
        <div style={{ width: 300 }}><AccountPicker accounts={ledgers} value={account} loading={chartLoading} onChange={(a) => setAccount(a?.name ?? null)} label="الحساب" /></div>
        <TextField size="small" type="date" label="من" value={from} onChange={(e) => setFrom(e.target.value)} InputLabelProps={{ shrink: true }} />
        <TextField size="small" type="date" label="إلى" value={to} onChange={(e) => setTo(e.target.value)} InputLabelProps={{ shrink: true }} />
        <TextField size="small" label="طرف (اسم)" value={party} onChange={(e) => setParty(e.target.value)} sx={{ width: 170 }} />
        <TextField select size="small" label="الوسم" value={tagId} onChange={(e) => setTagId(e.target.value)} sx={{ minWidth: 130 }}>
          <MenuItem value="">الكل</MenuItem>{tags.map((t) => <MenuItem key={t.id} value={t.id}>{t.name}</MenuItem>)}
        </TextField>
        <span style={{ marginInlineStart: 'auto' }}><ExportMenu build={buildExport} disabled={!data} /></span>
      </Stack>
      {loading ? <LinearProgress /> : null}
      {error ? <Alert severity="error">{error}</Alert> : null}
      {!account ? <Alert severity="info">اختر حساباً لعرض كشفه.</Alert> : null}
      {data ? (
        <>
          <Stack direction="row" gap={1} flexWrap="wrap">
            <Chip label={<>الرصيد الافتتاحي: <Money usd={data.opening} inline variant="body2" /></>} />
            <Chip color="primary" variant="outlined" label={<>مدين: <Money usd={data.totalDebit} inline variant="body2" /></>} />
            <Chip color="primary" variant="outlined" label={<>دائن: <Money usd={data.totalCredit} inline variant="body2" /></>} />
            <Chip color="success" label={<>الرصيد الختامي: <Money usd={data.closing} inline variant="body2" /></>} />
            {data.rootType ? <Chip variant="outlined" label={ROOT_LABEL[data.rootType] ?? data.rootType} /> : null}
            <Chip variant="outlined" label={`${data.totalCount.toLocaleString('en-US')} حركة`} />
          </Stack>
          {data.tagFiltered ? <Alert severity="info">الحركات الموسومة فقط؛ والرصيد تراكمي لها وليس رصيد الحساب.</Alert> : null}
          {data.totalCount > EXPORT_LIMIT ? <Alert severity="warning">الفترة تحوي أكثر من {EXPORT_LIMIT.toLocaleString('en-US')} حركة: التصدير يشمل أول {EXPORT_LIMIT.toLocaleString('en-US')} فقط. ضيّق المدة لتصدير الباقي.</Alert> : null}
          <DataTable
            columns={columns} rows={data.rows} getKey={(r) => r.glName ?? `${r.voucherNo}-${r.debit}-${r.credit}`} empty="لا توجد حركات في هذه الفترة" loading={loading}
            paging={{ page, pageSize, total: data.totalCount, onPage: setPage, onPageSize: (n) => { setPageSize(n); setPage(0); } }}
          />
        </>
      ) : null}
    </Stack>
  );
}

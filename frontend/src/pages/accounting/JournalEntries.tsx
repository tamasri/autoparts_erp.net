/**
 * القيود — manual accounting entries of any type (receipt, payment, contra, journal, ...): prepared as drafts, posted to the ERPNext
 * ledger, voided when wrong. Entries can be tagged and filtered by tag.
 */
import { useCallback, useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import type { AxiosResponse } from 'axios';
import { Alert, Button, Chip, MenuItem, Stack, Tab, Tabs, TextField, Tooltip, Typography } from '@mui/material';
import { accountingApi, type EntryType, type JournalEntryRow, type Tag } from '../../api/endpoints/accounting';
import { unwrapList } from '../../api/apiData';
import { usePagedList } from '../../hooks/usePagedList';
import { ACCOUNTING, useCan } from '../../hooks/useCan';
import { useChartAccounts } from '../../hooks/useChartAccounts';
import { extractApiError, toast } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';
import { num, ymd, type ExportDocument } from '../../lib/exportClient';
import PageHeader from '../../components/ui/PageHeader';
import DataTable, { type Column } from '../../components/ui/DataTable';
import ExportMenu from '../../components/ui/ExportMenu';
import ReasonDialog from '../../components/ui/ReasonDialog';
import StatusChip from '../../components/ui/StatusChip';
import EntryDialog from '../../features/accounting/EntryDialog';
import EntryTypesDialog from '../../features/accounting/EntryTypesDialog';
import EntryViewDialog from '../../features/accounting/EntryViewDialog';
import DeleteDocumentButton from '../../components/documents/DeleteDocumentButton';
import TagChips from '../../features/accounting/TagChips';
import TagsDialog from '../../features/accounting/TagsDialog';
import { SYNC_LABEL } from '../../features/accounting/labels';
import Money from '../../components/ui/Money';

const FILTERS = [{ key: '', label: 'الكل' }, { key: 'DRAFT', label: 'مسودات' }, { key: 'POSTED', label: 'مرحّلة' }, { key: 'VOID', label: 'ملغاة' }];

export default function JournalEntries(): JSX.Element {
  const canPost = useCan(ACCOUNTING.postEntries);
  const canConfigure = useCan(ACCOUNTING.manageAccounts);
  const [params, setParams] = useSearchParams();
  const { ledgers } = useChartAccounts();

  const [status, setStatus] = useState('');
  const [typeId, setTypeId] = useState('');
  const [tagId, setTagId] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [types, setTypes] = useState<EntryType[]>([]);
  const [tags, setTags] = useState<Tag[]>([]);
  const [editor, setEditor] = useState<{ open: boolean; id: string | null }>({ open: false, id: null });
  const [viewing, setViewing] = useState<string | null>(params.get('open'));
  const [voiding, setVoiding] = useState<JournalEntryRow | null>(null);
  const [typesOpen, setTypesOpen] = useState(false);
  const [tagsOpen, setTagsOpen] = useState(false);

  const list = usePagedList<JournalEntryRow>({
    errorMessage: 'تعذر تحميل القيود', deps: [status, typeId, tagId, from, to],
    fetcher: ({ page, pageSize, search }) => accountingApi.entries({
      page, pageSize, status: status || undefined, entryTypeId: typeId || undefined, tagId: tagId || undefined, from: from || undefined, to: to || undefined, search: search || undefined,
    }),
  });

  const loadLookups = useCallback(async () => {
    try {
      const [t, g] = await Promise.all([accountingApi.entryTypes(true), accountingApi.tags()]);
      setTypes(unwrapList<EntryType>(t.data)); setTags(unwrapList<Tag>(g.data));
    } catch (e: unknown) { toast.error(extractApiError(e, 'تعذر تحميل أنواع القيود')); }
  }, []);
  useEffect(() => { void loadLookups(); }, [loadLookups]);

  // A just-posted entry reaches the ledger through the outbox a few seconds later; keep the list current until it has.
  const waiting = list.items.some((r) => r.syncStatus === 'PENDING');
  useEffect(() => {
    if (!waiting) return undefined;
    const h = window.setTimeout(list.reload, 3000);
    return () => window.clearTimeout(h);
  }, [waiting, list.items, list.reload]);

  function closeView(): void { setViewing(null); if (params.has('open')) { params.delete('open'); setParams(params, { replace: true }); } }

  async function run(action: () => Promise<AxiosResponse>, done: string): Promise<void> {
    try { notifyResult(await action(), done); list.reload(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر تنفيذ العملية')); }
  }

  const buildExport = async (): Promise<ExportDocument> => {
    const res = await accountingApi.entries({ page: 1, pageSize: 200, status: status || undefined, entryTypeId: typeId || undefined, tagId: tagId || undefined, from: from || undefined, to: to || undefined, search: list.search || undefined });
    const data = (res.data as { data: { items: JournalEntryRow[]; totalCount: number } }).data;
    return {
      title: 'القيود المحاسبية', subtitle: `${data.totalCount} قيد${data.totalCount > 200 ? ' (أول 200)' : ''}`, fileName: 'journal-entries', fields: [],
      tables: [{
        columns: ['الرقم', 'النوع', 'التاريخ', 'البيان', 'المبلغ ($)', 'الحالة', 'وسوم'],
        rows: data.items.map((r) => [r.entryNumber, r.typeNameAr, ymd(r.entryDate), r.narration ?? '', num(r.totalUsd), r.status, r.tags.map((t) => t.name).join('، ')]), numericColumns: [4],
      }],
    };
  };

  const columns: Column<JournalEntryRow>[] = [
    { header: 'الرقم', nowrap: true, render: (r) => <Button size="small" onClick={() => setViewing(r.id)} sx={{ fontFamily: 'monospace', fontWeight: 700 }}>{r.entryNumber}</Button> },
    { header: 'النوع', render: (r) => r.typeNameAr },
    { header: 'التاريخ', nowrap: true, render: (r) => r.entryDate },
    { header: 'البيان', render: (r) => <Typography variant="body2" sx={{ maxWidth: 280 }} noWrap title={r.narration ?? ''}>{r.narration ?? '—'}</Typography> },
    { header: 'المبلغ', numeric: true, render: (r) => <Money usd={r.totalUsd} fontWeight={700} /> },
    { header: 'وسوم', render: (r) => <TagChips tags={r.tags} allTags={tags} canEdit={canPost} target={{ targetType: 'JOURNAL_ENTRY', targetKey: r.id }} onChanged={list.reload} /> },
    { header: 'الحالة', render: (r) => <StatusChip status={r.status} /> },
    {
      header: 'دفتر الأستاذ',
      render: (r) => {
        if (r.status === 'DRAFT') return '—';
        const s = SYNC_LABEL[r.syncStatus];
        return <Tooltip title={r.syncError ?? r.erpNextName ?? ''}><Chip size="small" color={s.color} label={s.label} /></Tooltip>;
      },
    },
    {
      header: '', nowrap: true,
      render: (r) => (
        <Stack direction="row" gap={0.5}>
          {canPost && r.status === 'DRAFT' ? <Button size="small" onClick={() => setEditor({ open: true, id: r.id })}>تعديل</Button> : null}
          {canPost && r.status === 'DRAFT' ? <Button size="small" color="success" onClick={() => void run(() => accountingApi.postEntry(r.id), 'تم الترحيل')}>ترحيل</Button> : null}
          {canPost && r.status === 'POSTED' ? <Button size="small" color="error" onClick={() => setVoiding(r)}>إلغاء</Button> : null}
          <DeleteDocumentButton kind="journal-entries" id={r.id} number={r.entryNumber} status={r.status} onDeleted={list.reload} />
        </Stack>
      ),
    },
  ];

  return (
    <>
      <PageHeader
        title="القيود" subtitle="سندات القبض والدفع والمناقلة وقيود اليومية — تُرحَّل إلى دفتر الأستاذ في ERPNext"
        actions={(
          <>
            <ExportMenu build={buildExport} />
            <Button size="small" variant="outlined" onClick={() => setTagsOpen(true)}>الوسوم</Button>
            <Button size="small" variant="outlined" onClick={() => setTypesOpen(true)}>أنواع القيود</Button>
            {canPost ? <Button size="small" variant="contained" onClick={() => setEditor({ open: true, id: null })}>＋ قيد جديد</Button> : null}
          </>
        )}
      />
      {list.error ? <Alert severity="error" sx={{ mb: 2 }}>{list.error}</Alert> : null}
      <Stack direction="row" gap={1.5} alignItems="center" flexWrap="wrap" sx={{ mb: 2 }}>
        <Tabs value={FILTERS.findIndex((f) => f.key === status)} onChange={(_, i: number) => setStatus(FILTERS[i].key)}>{FILTERS.map((f) => <Tab key={f.key} label={f.label} />)}</Tabs>
        <TextField select size="small" label="النوع" value={typeId} onChange={(e) => setTypeId(e.target.value)} sx={{ minWidth: 150 }}>
          <MenuItem value="">الكل</MenuItem>{types.map((t) => <MenuItem key={t.id} value={t.id}>{t.nameAr}</MenuItem>)}
        </TextField>
        <TextField select size="small" label="الوسم" value={tagId} onChange={(e) => setTagId(e.target.value)} sx={{ minWidth: 130 }}>
          <MenuItem value="">الكل</MenuItem>{tags.map((t) => <MenuItem key={t.id} value={t.id}>{t.name}</MenuItem>)}
        </TextField>
        <TextField size="small" type="date" label="من" value={from} onChange={(e) => setFrom(e.target.value)} InputLabelProps={{ shrink: true }} />
        <TextField size="small" type="date" label="إلى" value={to} onChange={(e) => setTo(e.target.value)} InputLabelProps={{ shrink: true }} />
        <TextField size="small" placeholder="بحث بالرقم أو البيان..." value={list.searchInput} onChange={(e) => list.setSearchInput(e.target.value)} sx={{ width: 240, mr: 'auto' }} />
      </Stack>
      <DataTable
        columns={columns} rows={list.items} getKey={(r) => r.id} loading={list.loading} empty="لا توجد قيود"
        paging={{ page: list.page - 1, pageSize: list.pageSize, total: list.totalCount, onPage: (p) => list.setPage(p + 1), onPageSize: list.changePageSize }}
      />

      <EntryDialog open={editor.open} editId={editor.id} types={types} accounts={ledgers} onClose={() => setEditor({ open: false, id: null })} onSaved={list.reload} />
      <EntryViewDialog entryId={viewing} onClose={closeView} onNavigate={setViewing} onDeleted={() => { closeView(); list.reload(); }} />
      <EntryTypesDialog open={typesOpen} types={types} canEdit={canConfigure} onClose={() => setTypesOpen(false)} onChanged={() => void loadLookups()} />
      <TagsDialog open={tagsOpen} onClose={() => setTagsOpen(false)} onChanged={() => { void loadLookups(); list.reload(); }} />
      <ReasonDialog
        open={Boolean(voiding)} title={`إلغاء القيد ${voiding?.entryNumber ?? ''}`} confirmLabel="إلغاء القيد" minLength={5} onClose={() => setVoiding(null)}
        onConfirm={async (reason) => { if (!voiding) return; await run(() => accountingApi.voidEntry(voiding.id, reason), 'أُلغي القيد وسيُلغى في دفتر الأستاذ'); setVoiding(null); }}
      />
    </>
  );
}

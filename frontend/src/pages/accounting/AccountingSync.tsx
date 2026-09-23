/** Hand-off to ERPNext: what has been sent and what failed (log), whether both sides agree (consistency), and ERPNext's own reference lists. */
import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Box, Button, Card, CardContent, Chip, Link, Paper, Stack, Tab, Tabs, Typography } from '@mui/material';
import { erpnextApi } from '../../api/endpoints/erpnext';
import { unwrapNode } from '../../api/apiData';
import { usePagedList } from '../../hooks/usePagedList';
import PageHeader from '../../components/ui/PageHeader';
import DataTable, { type Column } from '../../components/ui/DataTable';
import StatusChip from '../../components/ui/StatusChip';
import RoutedTabs from '../../components/ui/RoutedTabs';
import ErpNextConsistency from '../../features/accounting/ErpNextConsistency';
import ErpNextReference from '../../features/accounting/ErpNextReference';
import { DOCTYPE_LABEL, localRoute } from '../../features/accounting/erpnextLinks';
import { useAuthStore } from '../../stores/authStore';

type SyncRow = { localEntityType: string; localEntityId?: string; erpnextDoctype: string; status: string; erpnextName?: string | null; lastError?: string | null; attemptCount: number; updatedAt: string };
type Summary = { doctype: string; status: string; count: number };
type RecentError = { doctype: string; lastError?: string | null };
type SummaryPayload = { summary: Summary[]; recentErrors: RecentError[] };

const FILTERS = [{ key: '', label: 'الكل' }, { key: 'SYNCED', label: 'تمت' }, { key: 'FAILED', label: 'فشلت' }, { key: 'SKIPPED', label: 'متخطاة' }, { key: 'CANCELLED', label: 'ملغاة' }];
const STATUS_LABEL: Record<string, string> = { SYNCED: 'تمت', FAILED: 'فشلت', SKIPPED: 'متخطاة', CANCELLED: 'ملغاة', PENDING: 'قيد الانتظار' };

const COLUMNS: Column<SyncRow>[] = [
  { header: 'النوع', render: (r) => DOCTYPE_LABEL[r.erpnextDoctype] ?? r.erpnextDoctype },
  {
    header: 'السجل هنا',
    render: (r) => { const to = localRoute(r.localEntityType, r.localEntityId); return to ? <Link component={RouterLink} to={to}>فتح</Link> : ''; },
  },
  { header: 'المرجع في المحاسبة', render: (r) => <Typography component="span" sx={{ fontFamily: 'monospace', fontSize: 12 }}>{r.erpnextName ?? '—'}</Typography> },
  { header: 'الحالة', render: (r) => <StatusChip status={r.status === 'SYNCED' ? 'SUCCESS' : r.status} label={STATUS_LABEL[r.status] ?? r.status} /> },
  { header: 'الملاحظة', render: (r) => (r.lastError ? <Typography variant="caption" color="error" sx={{ display: 'block', maxWidth: 420 }} noWrap title={r.lastError}>{r.lastError}</Typography> : '') },
  { header: 'المحاولات', numeric: true, render: (r) => r.attemptCount },
  { header: 'آخر تحديث', nowrap: true, render: (r) => new Date(r.updatedAt).toLocaleString('ar') },
];

export default function AccountingSync(): JSX.Element {
  const isAdmin = useAuthStore((st) => (st.user?.roles ?? []).includes('SYSTEM_ADMIN'));
  const [syncing, setSyncing] = useState(false);
  const [actionError, setActionError] = useState('');
  const [info, setInfo] = useState('');

  async function run(): Promise<void> {
    setSyncing(true); setActionError(''); setInfo('');
    try { await erpnextApi.triggerSync(); setInfo('بدأت المزامنة في الخلفية. أعد الفحص بعد لحظات لرؤية النتيجة.'); }
    catch (e: unknown) {
      const r = e as { response?: { status?: number; data?: { error?: string; detail?: string } } };
      setActionError(r.response?.status === 409 ? 'تكامل ERPNext غير مفعّل في إعدادات الخادم' : r.response?.data?.error ?? r.response?.data?.detail ?? 'تعذر بدء المزامنة');
    } finally { setSyncing(false); }
  }

  return (
    <>
      <PageHeader
        title="مزامنة المحاسبة" subtitle="ما أُرسل إلى ERPNext، وهل يطابق ما فيه، وبيانات ERPNext المرجعية"
        actions={isAdmin ? <Button variant="contained" size="small" disabled={syncing} onClick={() => void run()}>{syncing ? 'جارٍ البدء...' : '⇄ مزامنة الآن'}</Button> : undefined}
      />
      {actionError ? <Alert severity="error" sx={{ mb: 2 }}>{actionError}</Alert> : null}
      {info ? <Alert severity="info" sx={{ mb: 2 }}>{info}</Alert> : null}
      <RoutedTabs tabs={[
        ...(isAdmin ? [{ key: 'log', label: 'سجل المزامنة', content: <SyncLog /> }] : []),
        { key: 'consistency', label: 'التطابق مع ERPNext', content: <ErpNextConsistency canSync={isAdmin} onSync={() => void run()} /> },
        { key: 'reference', label: 'بيانات ERPNext المرجعية', content: <ErpNextReference /> },
      ]} />
    </>
  );
}

/** What was sent and when, with the last error of anything that failed (the endpoints are for system administrators). */
function SyncLog(): JSX.Element {
  const [status, setStatus] = useState('');
  const [data, setData] = useState<SummaryPayload>({ summary: [], recentErrors: [] });

  const list = usePagedList<SyncRow>({ errorMessage: 'تعذر تحميل حالة المزامنة', deps: [status], fetcher: ({ page, pageSize }) => erpnextApi.getSyncLog(page, pageSize, status) });

  const loadSummary = useCallback(async () => {
    try { setData(unwrapNode<SummaryPayload>((await erpnextApi.getSummary()).data) ?? { summary: [], recentErrors: [] }); }
    catch { /* the list request already reports access or connectivity problems */ }
  }, []);
  useEffect(() => { void loadSummary(); }, [loadSummary]);

  const groups = useMemo(() => {
    const map = new Map<string, { synced: number; failed: number; other: number }>();
    for (const row of data.summary) {
      const g = map.get(row.doctype) ?? { synced: 0, failed: 0, other: 0 };
      if (row.status === 'SYNCED') g.synced += row.count; else if (row.status === 'FAILED') g.failed += row.count; else g.other += row.count;
      map.set(row.doctype, g);
    }
    return [...map.entries()];
  }, [data]);

  return (
    <>
      <Stack direction="row" justifyContent="flex-end" sx={{ mb: 1 }}><Button size="small" onClick={() => { list.reload(); void loadSummary(); }}>↻ تحديث</Button></Stack>
      {list.error ? <Alert severity="error" sx={{ mb: 2 }}>{list.error}</Alert> : null}

      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 2, mb: 3 }}>
        {groups.length === 0 ? <Paper variant="outlined" sx={{ p: 2, borderRadius: 3, color: 'text.secondary' }}>لا توجد عمليات مزامنة بعد</Paper> : groups.map(([doctype, g]) => (
          <Card key={doctype} variant="outlined" sx={{ borderRadius: 3, borderInlineStart: 4, borderInlineStartColor: g.failed > 0 ? 'error.main' : 'success.main' }}>
            <CardContent>
              <Typography fontWeight={700} sx={{ mb: 1 }}>{DOCTYPE_LABEL[doctype] ?? doctype}</Typography>
              <Stack direction="row" gap={0.5} flexWrap="wrap">
                <Chip size="small" color="success" variant="outlined" label={`تمت ${g.synced}`} />
                <Chip size="small" color={g.failed > 0 ? 'error' : 'default'} variant="outlined" label={`فشلت ${g.failed}`} />
                {g.other > 0 ? <Chip size="small" variant="outlined" label={`أخرى ${g.other}`} /> : null}
              </Stack>
            </CardContent>
          </Card>
        ))}
      </Box>

      {data.recentErrors.length > 0 ? (
        <Alert severity="error" sx={{ mb: 3 }}>
          <Typography fontWeight={700} sx={{ mb: 0.5 }}>آخر الأخطاء</Typography>
          {data.recentErrors.map((f, i) => <Typography key={`${f.doctype}-${i}`} variant="caption" sx={{ display: 'block' }}><strong>{DOCTYPE_LABEL[f.doctype] ?? f.doctype}:</strong> {f.lastError ?? '—'}</Typography>)}
        </Alert>
      ) : null}

      <Tabs value={FILTERS.findIndex((f) => f.key === status)} onChange={(_, i: number) => setStatus(FILTERS[i].key)} sx={{ mb: 1 }}>{FILTERS.map((f) => <Tab key={f.key} label={f.label} />)}</Tabs>
      <DataTable
        columns={COLUMNS} rows={list.items} getKey={(r) => `${r.erpnextDoctype}-${r.erpnextName ?? ''}-${r.updatedAt}`} loading={list.loading} empty="لا توجد سجلات"
        paging={{ page: list.page - 1, pageSize: list.pageSize, total: list.totalCount, onPage: (p) => list.setPage(p + 1), onPageSize: list.changePageSize }}
      />
    </>
  );
}

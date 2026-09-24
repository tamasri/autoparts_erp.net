/**
 * Document numbering: each series with its last number, how many documents are live and how many were deleted (every number is
 * one or the other, so "غير مفسّر" is always 0), and the log of deleted documents (who, when, why; their numbers are never reused).
 */
import { useEffect, useState } from 'react';
import { Alert, Chip, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material';
import PageHeader from '../../components/ui/PageHeader';
import DataTable, { type Column } from '../../components/ui/DataTable';
import StatusChip from '../../components/ui/StatusChip';
import { usePagedList } from '../../hooks/usePagedList';
import { unwrapList } from '../../api/apiData';
import { extractApiError } from '../../lib/toast';
import { documentsApi, type DeletedDocument, type SeriesHealth } from '../../api/endpoints/documents';

const COLUMNS: Column<DeletedDocument>[] = [
  { header: 'الرقم', nowrap: true, render: (r) => <Typography variant="body2" sx={{ fontFamily: 'monospace', fontWeight: 700 }}>{r.documentNumber}</Typography> },
  { header: 'السلسلة', render: (r) => r.seriesNameAr },
  { header: 'الحالة عند الحذف', render: (r) => <StatusChip status={r.statusAtDeletion} /> },
  { header: 'السبب', render: (r) => r.reason },
  { header: 'حذفه', render: (r) => r.deletedByName ?? '—' },
  { header: 'الوقت', nowrap: true, render: (r) => new Date(r.deletedAt).toLocaleString('ar') },
];

export default function Numbering(): JSX.Element {
  const [series, setSeries] = useState<SeriesHealth[]>([]);
  const [error, setError] = useState('');
  const [filter, setFilter] = useState('');

  useEffect(() => {
    documentsApi.numbering()
      .then((r) => setSeries(unwrapList<SeriesHealth>(r.data)))
      .catch((e: unknown) => setError(extractApiError(e, 'تعذر تحميل سلاسل الترقيم')));
  }, []);

  const list = usePagedList<DeletedDocument>({
    errorMessage: 'تعذر تحميل المستندات المحذوفة', deps: [filter],
    fetcher: ({ page, pageSize }) => documentsApi.deleted({ page, pageSize, series: filter || undefined }),
  });

  const broken = series.filter((s) => s.unexplained !== 0);

  return (
    <>
      <PageHeader title="الترقيم والمستندات المحذوفة" subtitle="كل رقم في كل سلسلة إما مستند قائم أو حذف مسجَّل — لا يُعاد استخدام رقم ولا يُحل مستند محل آخر" />
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      {series.length > 0 ? (
        <Alert severity={broken.length === 0 ? 'success' : 'error'} sx={{ mb: 2 }}>
          {broken.length === 0 ? 'الترقيم سليم في كل السلاسل: لا فجوة غير مفسّرة.' : `فجوات غير مفسّرة في: ${broken.map((s) => s.nameAr).join('، ')}`}
        </Alert>
      ) : null}
      <Paper variant="outlined" sx={{ borderRadius: 3, mb: 3, overflowX: 'auto' }}>
        <Table size="small">
          <TableHead>
            <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
              <TableCell>السلسلة</TableCell><TableCell>البادئة</TableCell><TableCell align="left">آخر رقم</TableCell>
              <TableCell align="left">قائمة</TableCell><TableCell align="left">محذوفة</TableCell><TableCell align="left">غير مفسّر</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {series.map((s) => (
              <TableRow key={s.code}>
                <TableCell>{s.nameAr}</TableCell>
                <TableCell sx={{ fontFamily: 'monospace' }}>{s.prefix}</TableCell>
                <TableCell align="left">{s.lastNumber}</TableCell>
                <TableCell align="left">{s.live}</TableCell>
                <TableCell align="left">{s.deleted}</TableCell>
                <TableCell align="left"><Chip size="small" color={s.unexplained === 0 ? 'success' : 'error'} label={s.unexplained} /></TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Paper>

      <Stack direction="row" gap={1.5} alignItems="center" sx={{ mb: 1.5 }}>
        <Typography variant="h6" fontWeight={800}>المستندات المحذوفة</Typography>
        <TextField select size="small" label="السلسلة" value={filter} onChange={(e) => setFilter(e.target.value)} sx={{ minWidth: 200, mr: 'auto' }}>
          <MenuItem value="">الكل</MenuItem>
          {series.filter((s) => s.deleted > 0).map((s) => <MenuItem key={s.code} value={s.code}>{s.nameAr}</MenuItem>)}
        </TextField>
      </Stack>
      {list.error ? <Alert severity="error" sx={{ mb: 2 }}>{list.error}</Alert> : null}
      <DataTable
        columns={COLUMNS} rows={list.items} getKey={(r) => r.id} loading={list.loading} empty="لم يُحذف أي مستند"
        paging={{ page: list.page - 1, pageSize: list.pageSize, total: list.totalCount, onPage: (p) => list.setPage(p + 1), onPageSize: list.changePageSize }}
      />
    </>
  );
}

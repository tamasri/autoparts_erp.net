/** The one list table for MUI screens: columns described as data, optional server paging, empty and loading states. */
import type { ReactNode } from 'react';
import { Paper, Table, TableBody, TableCell, TableContainer, TableHead, TablePagination, TableRow } from '@mui/material';
import { ARABIC_PAGINATION } from '../../lib/tablePagination';

export type Column<T> = {
  header: string;
  render: (row: T) => ReactNode;
  /** Numbers read better left-aligned in an RTL table. */
  numeric?: boolean;
  nowrap?: boolean;
  width?: number | string;
};

type Paging = {
  /** 0-based */
  page: number;
  pageSize: number;
  total: number;
  onPage: (page: number) => void;
  onPageSize: (size: number) => void;
};

type Props<T> = {
  columns: Column<T>[];
  rows: T[];
  getKey: (row: T) => string;
  loading?: boolean;
  empty?: string;
  paging?: Paging;
  dense?: boolean;
};

export default function DataTable<T>({ columns, rows, getKey, loading, empty = 'لا توجد بيانات', paging, dense = true }: Props<T>): JSX.Element {
  return (
    <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 3, opacity: loading ? 0.6 : 1, transition: 'opacity 120ms' }}>
      <Table size={dense ? 'small' : 'medium'}>
        <TableHead>
          <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover', whiteSpace: 'nowrap' } }}>
            {columns.map((c) => <TableCell key={c.header} align={c.numeric ? 'left' : undefined} sx={{ width: c.width }}>{c.header}</TableCell>)}
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.length === 0 ? (
            <TableRow><TableCell colSpan={columns.length} align="center" sx={{ py: 5, color: 'text.secondary' }}>{loading ? 'جارٍ التحميل...' : empty}</TableCell></TableRow>
          ) : rows.map((row) => (
            <TableRow key={getKey(row)} hover>
              {columns.map((c) => <TableCell key={c.header} align={c.numeric ? 'left' : undefined} sx={c.nowrap ? { whiteSpace: 'nowrap' } : undefined}>{c.render(row)}</TableCell>)}
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {paging ? (
        <TablePagination
          component="div" count={paging.total} page={paging.page} rowsPerPage={paging.pageSize} rowsPerPageOptions={[10, 20, 50, 100]}
          onPageChange={(_, p) => paging.onPage(p)} onRowsPerPageChange={(e) => paging.onPageSize(Number(e.target.value))} {...ARABIC_PAGINATION}
        />
      ) : null}
    </TableContainer>
  );
}

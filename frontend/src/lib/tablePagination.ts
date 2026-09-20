/** Arabic labels for every MUI TablePagination ("1–20 من 130"). */
export const ARABIC_PAGINATION = {
  labelRowsPerPage: 'عدد الصفوف',
  labelDisplayedRows: ({ from, to, count }: { from: number; to: number; count: number }): string => `${from}–${to} من ${count !== -1 ? count : `أكثر من ${to}`}`,
};

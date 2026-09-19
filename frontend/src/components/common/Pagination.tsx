type Props = {
  page: number;
  pageSize: number;
  totalCount: number;
  onPageChange: (page: number) => void;
  onPageSizeChange?: (size: number) => void;
  pageSizeOptions?: number[];
};

function pageWindow(page: number, totalPages: number): (number | '…')[] {
  if (totalPages <= 7) return Array.from({ length: totalPages }, (_, i) => i + 1);
  const pages: (number | '…')[] = [1];
  const start = Math.max(2, page - 1);
  const end = Math.min(totalPages - 1, page + 1);
  if (start > 2) pages.push('…');
  for (let p = start; p <= end; p += 1) pages.push(p);
  if (end < totalPages - 1) pages.push('…');
  pages.push(totalPages);
  return pages;
}

export default function Pagination({ page, pageSize, totalCount, onPageChange, onPageSizeChange, pageSizeOptions = [10, 20, 50, 100] }: Props): JSX.Element {
  const totalPages = Math.max(1, Math.ceil(totalCount / Math.max(1, pageSize)));
  const from = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(totalCount, page * pageSize);

  const btn = (active: boolean, disabled: boolean): React.CSSProperties => ({
    minWidth: 32, height: 32, padding: '0 8px', borderRadius: 'var(--radius-sm)',
    border: '1px solid var(--clr-border)', fontFamily: 'inherit', fontSize: 13, fontWeight: 600,
    cursor: disabled ? 'default' : 'pointer', opacity: disabled ? 0.45 : 1,
    background: active ? 'var(--clr-primary)' : 'var(--clr-surface)',
    color: active ? '#fff' : 'var(--txt-secondary)',
  });

  return (
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10, padding: '10px 18px', borderTop: '1px solid var(--clr-border)', fontSize: 12, color: 'var(--txt-muted)' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
        <span>{from.toLocaleString('en-US')}–{to.toLocaleString('en-US')} من {totalCount.toLocaleString('en-US')}</span>
        {onPageSizeChange ? (
          <select value={pageSize} onChange={(e) => onPageSizeChange(Number(e.target.value))} className="vex-input" style={{ width: 'auto', height: 32, padding: '0 8px' }} aria-label="عدد الصفوف">
            {pageSizeOptions.map((s) => <option key={s} value={s}>{s} / صفحة</option>)}
          </select>
        ) : null}
      </div>
      <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
        <button type="button" style={btn(false, page <= 1)} disabled={page <= 1} onClick={() => onPageChange(page - 1)} aria-label="السابق">›</button>
        {pageWindow(page, totalPages).map((p, i) => (p === '…'
          ? <span key={`gap-${i}`} style={{ padding: '0 4px' }}>…</span>
          : <button key={p} type="button" style={btn(p === page, false)} onClick={() => onPageChange(p)}>{p}</button>))}
        <button type="button" style={btn(false, page >= totalPages)} disabled={page >= totalPages} onClick={() => onPageChange(page + 1)} aria-label="التالي">‹</button>
      </div>
    </div>
  );
}

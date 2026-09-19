import { useState } from 'react';
import { auditApi } from '../../api/endpoints/audit';
import { usePagedList } from '../../hooks/usePagedList';
import Pagination from '../../components/common/Pagination';
import ErrorBanner from '../../components/common/ErrorBanner';
import StatusBadge from '../../components/common/StatusBadge';

type AuditRow = { id: string; createdAt?: string; actorUsername?: string; action?: string; module?: string; entityType?: string; status?: string };
type Filters = { module: string; entityType: string; from: string; to: string };

const emptyFilters: Filters = { module: '', entityType: '', from: '', to: '' };

export default function AuditLog(): JSX.Element {
  const [filters, setFilters] = useState<Filters>(emptyFilters);
  const [applied, setApplied] = useState<Filters>(emptyFilters);

  const list = usePagedList<AuditRow>({
    errorMessage: 'تعذر تحميل سجل التدقيق',
    pageSize: 50,
    deps: [applied],
    fetcher: ({ page, pageSize }) => {
      const params: Record<string, unknown> = { page, pageSize };
      if (applied.module.trim()) params.module = applied.module.trim();
      if (applied.entityType.trim()) params.entityType = applied.entityType.trim();
      if (applied.from) params.from = new Date(applied.from).toISOString();
      if (applied.to) params.to = new Date(applied.to).toISOString();
      return auditApi.getLogs(params);
    },
  });
  const { items: rows, error, loading } = list;

  function reset(): void { setFilters(emptyFilters); setApplied(emptyFilters); }

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">سجل التدقيق</h1>
          <div className="vex-page-header__breadcrumb">تتبع جميع العمليات والتغييرات في النظام</div>
        </div>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {/* Filter Card */}
      <div className="vex-card" style={{ marginBottom: 20 }}>
        <h2 className="vex-section-title">فلترة السجلات</h2>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))', gap: 16, marginBottom: 16 }}>
          <label className="vex-label">
            الوحدة (Module)
            <input value={filters.module} onChange={(e) => setFilters({ ...filters, module: e.target.value })} className="vex-input" placeholder="مثال: INVOICES" />
          </label>
          <label className="vex-label">
            نوع الكيان
            <input value={filters.entityType} onChange={(e) => setFilters({ ...filters, entityType: e.target.value })} className="vex-input" placeholder="مثال: Invoice" />
          </label>
          <label className="vex-label">
            من تاريخ
            <input type="date" value={filters.from} onChange={(e) => setFilters({ ...filters, from: e.target.value })} className="vex-input" />
          </label>
          <label className="vex-label">
            إلى تاريخ
            <input type="date" value={filters.to} onChange={(e) => setFilters({ ...filters, to: e.target.value })} className="vex-input" />
          </label>
        </div>
        <div style={{ display: 'flex', gap: 10 }}>
          <button type="button" onClick={() => setApplied(filters)} className="btn-primary">
            🔍 تطبيق الفلاتر
          </button>
          <button type="button" onClick={reset} className="btn-ghost">
            ↺ إعادة تعيين
          </button>
        </div>
      </div>

      {/* Audit Table */}
      {(
        <div className="vex-card vex-card--no-pad" style={{ opacity: loading ? 0.6 : 1, transition: 'opacity 120ms' }}>
          <div style={{ overflowX: 'auto' }}>
            <table className="vex-table">
              <thead>
                <tr>
                  <th>الوقت</th>
                  <th>المستخدم</th>
                  <th>الإجراء</th>
                  <th>الوحدة</th>
                  <th>الكيان</th>
                  <th>الحالة</th>
                </tr>
              </thead>
              <tbody>
                {rows.length === 0 ? (
                  <tr>
                    <td colSpan={6} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>
                      لا توجد سجلات مطابقة
                    </td>
                  </tr>
                ) : rows.map((row) => (
                  <tr key={row.id}>
                    <td style={{ color: 'var(--txt-secondary)', fontSize: 12, whiteSpace: 'nowrap' }}>
                      {row.createdAt ? new Date(row.createdAt).toLocaleString('ar') : '-'}
                    </td>
                    <td style={{ fontWeight: 600, color: 'var(--txt-primary)' }}>
                      <span style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                        <span style={{ width: 24, height: 24, borderRadius: '50%', background: 'var(--clr-primary-light)', color: 'var(--clr-primary)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 10, fontWeight: 700 }}>
                          {(row.actorUsername ?? '?')[0].toUpperCase()}
                        </span>
                        {row.actorUsername ?? '-'}
                      </span>
                    </td>
                    <td>
                      <span style={{ fontSize: 12, fontWeight: 700, color: 'var(--clr-primary)', background: 'var(--clr-primary-light)', padding: '2px 8px', borderRadius: 'var(--radius-sm)', fontFamily: 'monospace' }}>
                        {row.action ?? '-'}
                      </span>
                    </td>
                    <td>
                      <span style={{ fontSize: 12, color: 'var(--txt-muted)', background: 'var(--clr-surface-2)', padding: '2px 8px', borderRadius: 'var(--radius-sm)' }}>
                        {row.module ?? '-'}
                      </span>
                    </td>
                    <td style={{ color: 'var(--txt-secondary)', fontSize: 13 }}>{row.entityType ?? '-'}</td>
                    <td><StatusBadge status={row.status ?? 'UNKNOWN'} type="approval" /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination page={list.page} pageSize={list.pageSize} totalCount={list.totalCount} onPageChange={list.setPage} onPageSizeChange={list.changePageSize} />
        </div>
      )}
    </div>
  );
}

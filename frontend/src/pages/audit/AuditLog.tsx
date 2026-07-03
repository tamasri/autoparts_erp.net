import { useCallback, useEffect, useState } from 'react';
import { auditApi } from '../../api/endpoints/audit';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';
import StatusBadge from '../../components/common/StatusBadge';

type AuditRow = {
  id: string;
  createdAt?: string;
  actorUsername?: string;
  action?: string;
  module?: string;
  entityType?: string;
  status?: string;
};

type Filters = {
  module: string;
  entityType: string;
  from: string;
  to: string;
};

const emptyFilters: Filters = { module: '', entityType: '', from: '', to: '' };

export default function AuditLog(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<AuditRow[]>([]);
  const [filters, setFilters] = useState<Filters>(emptyFilters);

  const load = useCallback(async (f: Filters): Promise<void> => {
    setLoading(true);
    setError('');
    try {
      const params: Record<string, unknown> = { page: 1, pageSize: 50 };
      if (f.module.trim()) params.module = f.module.trim();
      if (f.entityType.trim()) params.entityType = f.entityType.trim();
      if (f.from) params.from = new Date(f.from).toISOString();
      if (f.to) params.to = new Date(f.to).toISOString();
      const res = await auditApi.getLogs(params);
      setRows(unwrapList<AuditRow>(res.data));
    } catch (e: unknown) {
      const r = e as { response?: { data?: { detail?: string; message?: string } } };
      setError(r.response?.data?.detail ?? r.response?.data?.message ?? 'تعذر تحميل سجل التدقيق');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load(emptyFilters);
  }, [load]);

  function reset(): void {
    setFilters(emptyFilters);
    void load(emptyFilters);
  }

  return (
    <div style={{ direction: 'rtl' }}>
      {error ? <ErrorBanner message={error} /> : null}

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', padding: '12px', marginBottom: '12px' }}>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))', gap: '12px' }}>
          <label style={lbl()}>الوحدة
            <input value={filters.module} onChange={(e) => setFilters({ ...filters, module: e.target.value })} style={inp()} placeholder="Module" />
          </label>
          <label style={lbl()}>نوع الكيان
            <input value={filters.entityType} onChange={(e) => setFilters({ ...filters, entityType: e.target.value })} style={inp()} placeholder="Entity Type" />
          </label>
          <label style={lbl()}>من تاريخ
            <input type="date" value={filters.from} onChange={(e) => setFilters({ ...filters, from: e.target.value })} style={inp()} />
          </label>
          <label style={lbl()}>إلى تاريخ
            <input type="date" value={filters.to} onChange={(e) => setFilters({ ...filters, to: e.target.value })} style={inp()} />
          </label>
        </div>
        <div style={{ display: 'flex', gap: '8px', marginTop: '12px' }}>
          <button type="button" onClick={() => void load(filters)} style={btn('#00796b')}>تطبيق الفلاتر</button>
          <button type="button" onClick={reset} style={btn('#607d8b')}>إعادة تعيين</button>
        </div>
      </div>

      {loading ? <LoadingSpinner /> : (
        <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
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
                <tr><td colSpan={6} style={{ textAlign: 'center', padding: '16px', color: '#777' }}>لا توجد سجلات مطابقة</td></tr>
              ) : rows.map((row) => (
                <tr key={row.id}>
                  <td>{row.createdAt ?? '-'}</td>
                  <td>{row.actorUsername ?? '-'}</td>
                  <td>{row.action ?? '-'}</td>
                  <td>{row.module ?? '-'}</td>
                  <td>{row.entityType ?? '-'}</td>
                  <td><StatusBadge status={row.status ?? 'UNKNOWN'} type="approval" /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function btn(bg: string): React.CSSProperties {
  return { border: 'none', borderRadius: '8px', background: bg, color: '#fff', padding: '8px 14px', cursor: 'pointer', fontSize: '13px' };
}
function lbl(): React.CSSProperties {
  return { display: 'flex', flexDirection: 'column', gap: '4px', fontSize: '13px', color: '#333' };
}
function inp(): React.CSSProperties {
  return { padding: '8px', border: '1px solid #b0bec5', borderRadius: '8px' };
}

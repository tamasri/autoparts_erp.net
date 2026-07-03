import { useEffect, useState } from 'react';
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


export default function AuditLog(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<AuditRow[]>([]);

  useEffect(() => {
    let mounted = true;
    async function load(): Promise<void> {
      setLoading(true);
      setError('');
      try {
        const res = await auditApi.getLogs({ page: 1, pageSize: 50 });
        if (mounted) setRows(unwrapList<AuditRow>(res.data));
      } catch (e: unknown) {
        if (!mounted) return;
        const msg = (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.detail
          ?? (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.message
          ?? 'تعذر تحميل سجل التدقيق';
        setError(msg);
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void load();
    return () => {
      mounted = false;
    };
  }, []);

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      {error ? <ErrorBanner message={error} /> : null}
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
            {rows.map((row) => (
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
    </div>
  );
}

import { useEffect, useState } from 'react';
import { approvalsApi } from '../../api/endpoints/approvals';
import { unwrapList } from '../../api/apiData';
import EmptyState from '../../components/common/EmptyState';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';
import StatusBadge from '../../components/common/StatusBadge';

type Approval = {
  id: string;
  requestType?: string;
  requesterUsername?: string;
  requesterName?: string;
  createdAt?: string;
  expiresAt?: string;
  status?: string;
};

export default function Approvals(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<Approval[]>([]);

  async function load(): Promise<void> {
    setLoading(true);
    setError('');
    try {
      const res = await approvalsApi.getPending(1, 50);
      setRows(unwrapList<Approval>(res.data));
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.detail
        ?? (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.message
        ?? 'تعذر تحميل الطلبات';
      setError(msg);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      {error ? <ErrorBanner message={error} /> : null}
      {rows.length === 0 ? (
        <EmptyState icon="✅" message="لا توجد طلبات موافقة معلقة" />
      ) : (
        <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th>النوع</th>
                <th>الطالب</th>
                <th>التاريخ</th>
                <th>ينتهي في</th>
                <th>الحالة</th>
                <th>إجراءات</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.id}>
                  <td>{row.requestType ?? '-'}</td>
                  <td>{row.requesterName ?? row.requesterUsername ?? '-'}</td>
                  <td>{row.createdAt ?? '-'}</td>
                  <td>{row.expiresAt ?? '-'}</td>
                  <td><StatusBadge status={row.status ?? 'PENDING'} type="approval" /></td>
                  <td style={{ display: 'flex', gap: '6px' }}>
                    <button
                      type="button"
                      onClick={async () => { await approvalsApi.approve(row.id); await load(); }}
                      style={{ border: 'none', borderRadius: '6px', background: '#2e7d32', color: '#fff', padding: '4px 8px' }}
                    >
                      ✓
                    </button>
                    <button
                      type="button"
                      onClick={async () => {
                        const reason = window.prompt('سبب الرفض') ?? '';
                        if (!reason.trim()) return;
                        await approvalsApi.reject(row.id, reason.trim());
                        await load();
                      }}
                      style={{ border: 'none', borderRadius: '6px', background: '#c62828', color: '#fff', padding: '4px 8px' }}
                    >
                      ✗
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

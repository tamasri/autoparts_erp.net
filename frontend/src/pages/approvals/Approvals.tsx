import { useEffect, useState } from 'react';
import { approvalsApi } from '../../api/endpoints/approvals';
import { unwrapList } from '../../api/apiData';
import { toast, extractApiError } from '../../lib/toast';
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
  const [busy, setBusy] = useState('');

  async function load(): Promise<void> {
    setLoading(true);
    setError('');
    try {
      const res = await approvalsApi.getPending(1, 50);
      setRows(unwrapList<Approval>(res.data));
    } catch (e: unknown) {
      setError(extractApiError(e, 'تعذر تحميل الطلبات'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void load(); }, []);

  async function approve(id: string): Promise<void> {
    setBusy(id);
    try {
      await approvalsApi.approve(id);
      toast.success('تمت الموافقة بنجاح');
      await load();
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر الموافقة على الطلب'));
    } finally {
      setBusy('');
    }
  }

  async function reject(id: string): Promise<void> {
    const reason = window.prompt('سبب الرفض') ?? '';
    if (!reason.trim()) return;
    setBusy(id);
    try {
      await approvalsApi.reject(id, reason.trim());
      toast.success('تم رفض الطلب');
      await load();
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر رفض الطلب'));
    } finally {
      setBusy('');
    }
  }

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
                <th style={th()}>النوع</th>
                <th style={th()}>الطالب</th>
                <th style={th()}>التاريخ</th>
                <th style={th()}>ينتهي في</th>
                <th style={th()}>الحالة</th>
                <th style={th()}>إجراءات</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.id}>
                  <td style={td()}>{row.requestType ?? '-'}</td>
                  <td style={td()}>{row.requesterName ?? row.requesterUsername ?? '-'}</td>
                  <td style={td()}>{row.createdAt ?? '-'}</td>
                  <td style={td()}>{row.expiresAt ?? '-'}</td>
                  <td style={td()}><StatusBadge status={row.status ?? 'PENDING'} type="approval" /></td>
                  <td style={{ ...td(), whiteSpace: 'nowrap' }}>
                    <button
                      type="button"
                      disabled={busy === row.id}
                      onClick={() => void approve(row.id)}
                      style={btn('#2e7d32')}
                    >
                      {busy === row.id ? '...' : '✓ موافقة'}
                    </button>
                    <button
                      type="button"
                      disabled={busy === row.id}
                      onClick={() => void reject(row.id)}
                      style={btn('#c62828')}
                    >
                      {busy === row.id ? '...' : '✗ رفض'}
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

function btn(bg: string): React.CSSProperties {
  return { border: 'none', borderRadius: '6px', background: bg, color: '#fff', padding: '5px 10px', margin: '0 3px', cursor: 'pointer', fontSize: '13px' };
}
function th(): React.CSSProperties {
  return { padding: '10px 12px', textAlign: 'right', background: '#f5f5f5', borderBottom: '1px solid #e0e0e0' };
}
function td(): React.CSSProperties {
  return { padding: '10px 12px', borderBottom: '1px solid #f0f0f0' };
}

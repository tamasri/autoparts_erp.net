import { useState } from 'react';
import { approvalsApi } from '../../api/endpoints/approvals';
import { usePagedList } from '../../hooks/usePagedList';
import Pagination from '../../components/common/Pagination';
import { toast, extractApiError } from '../../lib/toast';
import EmptyState from '../../components/common/EmptyState';
import ErrorBanner from '../../components/common/ErrorBanner';
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
  const list = usePagedList<Approval>({
    errorMessage: 'تعذر تحميل الطلبات',
    fetcher: ({ page, pageSize }) => approvalsApi.getPending(page, pageSize),
  });
  const { items: rows, error, loading } = list;
  const [busy, setBusy] = useState('');

  const load = async (): Promise<void> => { list.reload(); };

  async function approve(id: string): Promise<void> {
    setBusy(id);
    try { await approvalsApi.approve(id); toast.success('تمت الموافقة بنجاح'); await load(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر الموافقة على الطلب')); }
    finally { setBusy(''); }
  }

  async function reject(id: string): Promise<void> {
    const reason = window.prompt('سبب الرفض') ?? '';
    if (!reason.trim()) return;
    setBusy(id);
    try { await approvalsApi.reject(id, reason.trim()); toast.success('تم رفض الطلب'); await load(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر رفض الطلب')); }
    finally { setBusy(''); }
  }

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">طلبات الموافقة</h1>
          <div className="vex-page-header__breadcrumb">مراجعة والبت في الطلبات المعلّقة</div>
        </div>
        {rows.length > 0 && (
          <div style={{ background: '#fff7ed', color: '#ea580c', border: '1px solid #fed7aa', borderRadius: 'var(--radius-pill)', padding: '6px 14px', fontSize: 13, fontWeight: 700 }}>
            ⏳ معلّق: {list.totalCount}
          </div>
        )}
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {rows.length === 0 && !loading ? (
        <div className="vex-card" style={{ textAlign: 'center', padding: '48px 0' }}>
          <EmptyState icon="✅" message="لا توجد طلبات موافقة معلقة" />
        </div>
      ) : (
        <div className="vex-card vex-card--no-pad" style={{ opacity: loading ? 0.6 : 1, transition: 'opacity 120ms' }}>
          <div style={{ overflowX: 'auto' }}>
            <table className="vex-table">
              <thead>
                <tr>
                  <th>نوع الطلب</th>
                  <th>الطالب</th>
                  <th>تاريخ الطلب</th>
                  <th>ينتهي في</th>
                  <th>الحالة</th>
                  <th>إجراءات</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.id}>
                    <td>
                      <span style={{ fontSize: 12, color: 'var(--txt-muted)', background: 'var(--clr-surface-2)', padding: '2px 10px', borderRadius: 'var(--radius-sm)', fontWeight: 600 }}>
                        {row.requestType ?? '-'}
                      </span>
                    </td>
                    <td style={{ fontWeight: 600, color: 'var(--txt-primary)' }}>
                      {row.requesterName ?? row.requesterUsername ?? '-'}
                    </td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{row.createdAt ?? '-'}</td>
                    <td style={{ color: 'var(--clr-danger)', fontWeight: 600, fontSize: 13 }}>{row.expiresAt ?? '-'}</td>
                    <td><StatusBadge status={row.status ?? 'PENDING'} type="approval" /></td>
                    <td style={{ whiteSpace: 'nowrap' }}>
                      <button
                        type="button"
                        disabled={busy === row.id}
                        onClick={() => void approve(row.id)}
                        className="btn-success"
                        style={{ padding: '5px 14px', fontSize: 12, marginLeft: 8 }}
                      >
                        {busy === row.id ? '...' : '✓ موافقة'}
                      </button>
                      <button
                        type="button"
                        disabled={busy === row.id}
                        onClick={() => void reject(row.id)}
                        className="btn-danger"
                        style={{ padding: '5px 14px', fontSize: 12 }}
                      >
                        {busy === row.id ? '...' : '✕ رفض'}
                      </button>
                    </td>
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

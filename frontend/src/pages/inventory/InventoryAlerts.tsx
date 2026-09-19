import { useEffect, useState } from 'react';
import { inventoryAlertsApi } from '../../api/endpoints/inventoryAlerts';
import { unwrapList } from '../../api/apiData';
import { toast, extractApiError } from '../../lib/toast';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

type Alert = {
  id: string;
  itemId: string;
  alertType: string;
  severity: string;
  message: string;
  thresholdValue?: number;
  currentValue?: number;
  status: string;
  createdAt: string;
};

const SEVERITY_CONFIG: Record<string, { bg: string; color: string; icon: string }> = {
  CRITICAL: { bg: '#fef2f2', color: 'var(--clr-danger)', icon: '🔴' },
  HIGH:     { bg: '#fff7ed', color: '#ea580c', icon: '🟠' },
  MEDIUM:   { bg: '#fefce8', color: '#ca8a04', icon: '🟡' },
  LOW:      { bg: '#f0fdf4', color: '#22c55e', icon: '🟢' },
};

export default function InventoryAlerts(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<Alert[]>([]);
  const [busy, setBusy] = useState<string>('');

  async function load(): Promise<void> {
    setLoading(true); setError('');
    try { const res = await inventoryAlertsApi.list(); setRows(unwrapList<Alert>(res.data)); }
    catch (e: unknown) { setError(extractApiError(e, 'تعذر تحميل التنبيهات')); }
    finally { setLoading(false); }
  }

  useEffect(() => { void load(); }, []);

  async function acknowledge(id: string): Promise<void> {
    setBusy(id);
    try { await inventoryAlertsApi.acknowledge(id); toast.success('تم تأكيد التنبيه'); await load(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر تأكيد التنبيه')); }
    finally { setBusy(''); }
  }

  async function resolve(id: string): Promise<void> {
    const note = window.prompt('ملاحظة الإغلاق (اختياري):') ?? undefined;
    setBusy(id);
    try { await inventoryAlertsApi.resolve(id, note); toast.success('تم إغلاق التنبيه'); await load(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر إغلاق التنبيه')); }
    finally { setBusy(''); }
  }

  const criticalCount = rows.filter((r) => r.severity === 'CRITICAL' && r.status !== 'RESOLVED').length;
  const activeCount = rows.filter((r) => r.status !== 'RESOLVED').length;

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">تنبيهات المخزون</h1>
          <div className="vex-page-header__breadcrumb">مراقبة مستويات المخزون والتنبيهات النشطة</div>
        </div>
        <div style={{ display: 'flex', gap: 10 }}>
          {criticalCount > 0 && (
            <div style={{ background: '#fef2f2', color: 'var(--clr-danger)', border: '1px solid #fecaca', borderRadius: 'var(--radius-pill)', padding: '6px 14px', fontSize: 13, fontWeight: 700 }}>
              🔴 حرج: {criticalCount}
            </div>
          )}
          {activeCount > 0 && (
            <div style={{ background: '#fff7ed', color: '#ea580c', border: '1px solid #fed7aa', borderRadius: 'var(--radius-pill)', padding: '6px 14px', fontSize: 13, fontWeight: 700 }}>
              ⚠ نشط: {activeCount}
            </div>
          )}
        </div>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      <div className="vex-card vex-card--no-pad">
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead>
              <tr>
                <th>الخطورة</th>
                <th>النوع</th>
                <th>الرسالة</th>
                <th>الحد</th>
                <th>الحالي</th>
                <th>الحالة</th>
                <th>إجراءات</th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 ? (
                <tr>
                  <td colSpan={7} style={{ textAlign: 'center', padding: '40px 0' }}>
                    <div style={{ fontSize: 32, marginBottom: 8 }}>✅</div>
                    <div style={{ color: 'var(--txt-muted)', fontSize: 14 }}>لا توجد تنبيهات نشطة</div>
                  </td>
                </tr>
              ) : rows.map((a) => {
                const sev = SEVERITY_CONFIG[a.severity] ?? { bg: '#f8fafc', color: 'var(--txt-muted)', icon: '⚪' };
                return (
                  <tr key={a.id} style={{ background: a.status === 'RESOLVED' ? undefined : sev.bg }}>
                    <td>
                      <span style={{ color: sev.color, fontWeight: 700, fontSize: 13 }}>
                        {sev.icon} {a.severity}
                      </span>
                    </td>
                    <td>
                      <span style={{ fontSize: 12, color: 'var(--txt-muted)', background: 'var(--clr-surface-2)', padding: '2px 8px', borderRadius: 'var(--radius-sm)' }}>
                        {a.alertType}
                      </span>
                    </td>
                    <td style={{ color: 'var(--txt-primary)', maxWidth: 280 }}>{a.message}</td>
                    <td style={{ color: 'var(--txt-muted)', fontSize: 13 }}>{a.thresholdValue ?? '-'}</td>
                    <td style={{ fontWeight: 600 }}>{a.currentValue ?? '-'}</td>
                    <td>
                      {a.status === 'RESOLVED'
                        ? <span className="badge badge--success">مغلق</span>
                        : a.status === 'ACKNOWLEDGED'
                          ? <span className="badge badge--warning">مؤكد</span>
                          : <span className="badge badge--danger">مفتوح</span>}
                    </td>
                    <td style={{ whiteSpace: 'nowrap' }}>
                      {a.status !== 'RESOLVED' ? (
                        <>
                          <button type="button" disabled={busy === a.id} onClick={() => void acknowledge(a.id)} className="btn-secondary" style={{ padding: '5px 12px', fontSize: 12, marginLeft: 6 }}>
                            ✓ تأكيد
                          </button>
                          <button type="button" disabled={busy === a.id} onClick={() => void resolve(a.id)} className="btn-success" style={{ padding: '5px 12px', fontSize: 12 }}>
                            ✕ إغلاق
                          </button>
                        </>
                      ) : <span style={{ color: 'var(--txt-muted)', fontSize: 12 }}>—</span>}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

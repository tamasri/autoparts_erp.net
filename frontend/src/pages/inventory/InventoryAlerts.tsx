import { useEffect, useState } from 'react';
import { inventoryAlertsApi } from '../../api/endpoints/inventoryAlerts';
import { unwrapList } from '../../api/apiData';
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

function extractError(e: unknown, fallback: string): string {
  const r = e as { response?: { data?: { detail?: string; message?: string } } };
  return r.response?.data?.detail ?? r.response?.data?.message ?? fallback;
}

const severityColor: Record<string, string> = {
  CRITICAL: '#c62828',
  HIGH: '#e65100',
  MEDIUM: '#f9a825',
  LOW: '#2e7d32',
};

export default function InventoryAlerts(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<Alert[]>([]);
  const [busy, setBusy] = useState<string>('');

  async function load(): Promise<void> {
    setLoading(true);
    setError('');
    try {
      const res = await inventoryAlertsApi.list();
      setRows(unwrapList<Alert>(res.data));
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر تحميل التنبيهات'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  async function acknowledge(id: string): Promise<void> {
    setBusy(id);
    try {
      await inventoryAlertsApi.acknowledge(id);
      await load();
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر تأكيد التنبيه'));
    } finally {
      setBusy('');
    }
  }

  async function resolve(id: string): Promise<void> {
    const note = window.prompt('ملاحظة الإغلاق (اختياري):') ?? undefined;
    setBusy(id);
    try {
      await inventoryAlertsApi.resolve(id, note);
      await load();
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر إغلاق التنبيه'));
    } finally {
      setBusy('');
    }
  }

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      <h2 style={{ marginTop: 0 }}>تنبيهات المخزون</h2>
      {error ? <ErrorBanner message={error} /> : null}
      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th>النوع</th>
              <th>الخطورة</th>
              <th>الرسالة</th>
              <th>الحد</th>
              <th>الحالي</th>
              <th>الحالة</th>
              <th>إجراءات</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr><td colSpan={7} style={{ textAlign: 'center', padding: '16px', color: '#777' }}>لا توجد تنبيهات</td></tr>
            ) : rows.map((a) => (
              <tr key={a.id}>
                <td>{a.alertType}</td>
                <td style={{ color: severityColor[a.severity] ?? '#333', fontWeight: 700 }}>{a.severity}</td>
                <td>{a.message}</td>
                <td>{a.thresholdValue ?? '-'}</td>
                <td>{a.currentValue ?? '-'}</td>
                <td>{a.status}</td>
                <td style={{ whiteSpace: 'nowrap' }}>
                  {a.status !== 'RESOLVED' ? (
                    <>
                      <button type="button" disabled={busy === a.id} onClick={() => void acknowledge(a.id)} style={btn('#00796b')}>تأكيد</button>
                      <button type="button" disabled={busy === a.id} onClick={() => void resolve(a.id)} style={btn('#004d40')}>إغلاق</button>
                    </>
                  ) : <span style={{ color: '#2e7d32' }}>مغلق</span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function btn(bg: string): React.CSSProperties {
  return { border: 'none', borderRadius: '8px', background: bg, color: '#fff', padding: '6px 10px', margin: '0 4px', cursor: 'pointer', fontSize: '13px' };
}

import { useEffect, useState } from 'react';
import { cycleCountsApi, type CreateCycleCountPlan, type RecordCycleCountLine } from '../../api/endpoints/cycleCounts';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';
import StatusBadge from '../../components/common/StatusBadge';

type CycleCountPlan = {
  id: string;
  planNo: string;
  warehouseId: string;
  scopeType: string;
  status: string;
  scheduledFor?: string;
};

const today = new Date().toISOString().slice(0, 10);
const emptyForm: CreateCycleCountPlan = { warehouseId: '', scopeType: 'FULL', scheduledFor: today };

function extractError(e: unknown, fallback: string): string {
  const r = e as { response?: { data?: { detail?: string; message?: string } } };
  return r.response?.data?.detail ?? r.response?.data?.message ?? fallback;
}

export default function CycleCounts(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<CycleCountPlan[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState('');
  const [form, setForm] = useState<CreateCycleCountPlan>(emptyForm);
  const [recordFor, setRecordFor] = useState('');
  const [recordJson, setRecordJson] = useState('[\n  { "lineId": "", "countedQty": 0 }\n]');

  async function load(): Promise<void> {
    setLoading(true); setError('');
    try { const res = await cycleCountsApi.list(1, 100); setRows(unwrapList<CycleCountPlan>(res.data)); }
    catch (e: unknown) { setError(extractError(e, 'تعذر تحميل خطط الجرد')); }
    finally { setLoading(false); }
  }

  useEffect(() => { void load(); }, []);

  async function create(): Promise<void> {
    if (!form.warehouseId.trim()) { setError('معرّف المستودع مطلوب'); return; }
    setBusy('create');
    try {
      await cycleCountsApi.create({ warehouseId: form.warehouseId.trim(), scopeType: form.scopeType, scopeFilterJson: form.scopeFilterJson?.trim() || undefined, scheduledFor: form.scheduledFor });
      setForm(emptyForm); setShowForm(false); await load();
    } catch (e: unknown) { setError(extractError(e, 'تعذر إنشاء خطة الجرد')); }
    finally { setBusy(''); }
  }

  async function submitRecord(id: string): Promise<void> {
    let parsed: RecordCycleCountLine[];
    try { parsed = JSON.parse(recordJson) as RecordCycleCountLine[]; if (!Array.isArray(parsed)) throw new Error('not-array'); }
    catch { setError('صيغة JSON غير صحيحة لأسطر الجرد'); return; }
    setBusy(id);
    try { await cycleCountsApi.record(id, parsed); setRecordFor(''); await load(); }
    catch (e: unknown) { setError(extractError(e, 'تعذر تسجيل نتائج الجرد')); }
    finally { setBusy(''); }
  }

  async function approveVariance(id: string): Promise<void> {
    setBusy(id);
    try { await cycleCountsApi.approveVariance(id); await load(); }
    catch (e: unknown) { setError(extractError(e, 'تعذر اعتماد الفروقات')); }
    finally { setBusy(''); }
  }

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">الجرد الدوري</h1>
          <div className="vex-page-header__breadcrumb">إنشاء وإدارة خطط الجرد الدوري للمخزون</div>
        </div>
        <button type="button" onClick={() => setShowForm((s) => !s)} className={showForm ? 'btn-ghost' : 'btn-primary'}>
          {showForm ? '✕ إلغاء' : '＋ خطة جرد'}
        </button>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {showForm ? (
        <div className="vex-card" style={{ marginBottom: 20 }}>
          <h2 className="vex-section-title">خطة جرد جديدة</h2>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 16, marginBottom: 20 }}>
            <label className="vex-label">
              المستودع *
              <input value={form.warehouseId} onChange={(e) => setForm({ ...form, warehouseId: e.target.value })} className="vex-input" placeholder="Warehouse ID" />
            </label>
            <label className="vex-label">
              نطاق الجرد
              <select value={form.scopeType} onChange={(e) => setForm({ ...form, scopeType: e.target.value })} className="vex-select">
                <option value="FULL">كامل</option>
                <option value="LOCATION">حسب الموقع</option>
                <option value="CATEGORY">حسب الفئة</option>
                <option value="ABC">تحليل ABC</option>
              </select>
            </label>
            <label className="vex-label">
              تاريخ التنفيذ
              <input type="date" value={form.scheduledFor} onChange={(e) => setForm({ ...form, scheduledFor: e.target.value })} className="vex-input" />
            </label>
            <label className="vex-label">
              مرشّح النطاق (JSON)
              <input value={form.scopeFilterJson ?? ''} onChange={(e) => setForm({ ...form, scopeFilterJson: e.target.value })} className="vex-input" placeholder='{"locationId":"..."}' />
            </label>
          </div>
          <button type="button" disabled={busy === 'create'} onClick={() => void create()} className="btn-primary">
            💾 حفظ الخطة
          </button>
        </div>
      ) : null}

      <div className="vex-card vex-card--no-pad">
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead>
              <tr>
                <th>رقم الخطة</th>
                <th>المستودع</th>
                <th>النطاق</th>
                <th>التاريخ</th>
                <th>الحالة</th>
                <th>إجراءات</th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 ? (
                <tr><td colSpan={6} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>لا توجد خطط جرد</td></tr>
              ) : rows.map((p) => (
                <>
                  <tr key={p.id}>
                    <td style={{ fontWeight: 600, color: 'var(--clr-primary)' }}>{p.planNo}</td>
                    <td><span className="badge badge--draft">{p.warehouseId.slice(0, 8)}</span></td>
                    <td><span style={{ fontSize: 12, color: 'var(--txt-secondary)', background: 'var(--clr-surface-2)', padding: '2px 8px', borderRadius: 'var(--radius-sm)' }}>{p.scopeType}</span></td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{p.scheduledFor ? new Date(p.scheduledFor).toLocaleDateString('ar') : '-'}</td>
                    <td><StatusBadge status={p.status} type="invoice" /></td>
                    <td style={{ whiteSpace: 'nowrap' }}>
                      <button type="button" disabled={busy === p.id} onClick={() => setRecordFor((v) => (v === p.id ? '' : p.id))} className="btn-secondary" style={{ padding: '5px 12px', fontSize: 12, marginLeft: 6 }}>
                        {recordFor === p.id ? '▲ إخفاء' : '📋 تسجيل الجرد'}
                      </button>
                      <button type="button" disabled={busy === p.id} onClick={() => void approveVariance(p.id)} className="btn-success" style={{ padding: '5px 12px', fontSize: 12 }}>
                        ✓ اعتماد الفروقات
                      </button>
                    </td>
                  </tr>
                  {recordFor === p.id ? (
                    <tr key={`${p.id}-record`}>
                      <td colSpan={6} style={{ background: 'var(--clr-surface-2)', padding: '14px 20px' }}>
                        <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--txt-muted)', marginBottom: 8 }}>
                          أسطر الجرد (JSON) — [{"{"} lineId, countedQty {"}"}]
                        </div>
                        <textarea
                          value={recordJson}
                          onChange={(e) => setRecordJson(e.target.value)}
                          rows={5}
                          className="vex-input"
                          style={{ width: '100%', fontFamily: 'monospace', direction: 'ltr', fontSize: 13 }}
                        />
                        <div style={{ marginTop: 10 }}>
                          <button type="button" disabled={busy === p.id} onClick={() => void submitRecord(p.id)} className="btn-primary">
                            📤 إرسال النتائج
                          </button>
                        </div>
                      </td>
                    </tr>
                  ) : null}
                </>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

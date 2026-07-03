import { useEffect, useState } from 'react';
import {
  cycleCountsApi,
  type CreateCycleCountPlan,
  type RecordCycleCountLine,
} from '../../api/endpoints/cycleCounts';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

type CycleCountPlan = {
  id: string;
  planNo: string;
  warehouseId: string;
  scopeType: string;
  status: string;
  scheduledFor?: string;
};

function extractError(e: unknown, fallback: string): string {
  const r = e as { response?: { data?: { detail?: string; message?: string } } };
  return r.response?.data?.detail ?? r.response?.data?.message ?? fallback;
}

const today = new Date().toISOString().slice(0, 10);

const emptyForm: CreateCycleCountPlan = {
  warehouseId: '',
  scopeType: 'FULL',
  scheduledFor: today,
};

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
    setLoading(true);
    setError('');
    try {
      const res = await cycleCountsApi.list(1, 100);
      setRows(unwrapList<CycleCountPlan>(res.data));
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر تحميل خطط الجرد'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  async function create(): Promise<void> {
    if (!form.warehouseId.trim()) {
      setError('معرّف المستودع مطلوب');
      return;
    }
    setBusy('create');
    try {
      const payload: CreateCycleCountPlan = {
        warehouseId: form.warehouseId.trim(),
        scopeType: form.scopeType,
        scopeFilterJson: form.scopeFilterJson?.trim() || undefined,
        scheduledFor: form.scheduledFor,
      };
      await cycleCountsApi.create(payload);
      setForm(emptyForm);
      setShowForm(false);
      await load();
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر إنشاء خطة الجرد'));
    } finally {
      setBusy('');
    }
  }

  async function submitRecord(id: string): Promise<void> {
    let parsed: RecordCycleCountLine[];
    try {
      parsed = JSON.parse(recordJson) as RecordCycleCountLine[];
      if (!Array.isArray(parsed)) throw new Error('not-array');
    } catch {
      setError('صيغة JSON غير صحيحة لأسطر الجرد');
      return;
    }
    setBusy(id);
    try {
      await cycleCountsApi.record(id, parsed);
      setRecordFor('');
      await load();
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر تسجيل نتائج الجرد'));
    } finally {
      setBusy('');
    }
  }

  async function approveVariance(id: string): Promise<void> {
    setBusy(id);
    try {
      await cycleCountsApi.approveVariance(id);
      await load();
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر اعتماد الفروقات'));
    } finally {
      setBusy('');
    }
  }

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <h2 style={{ marginTop: 0 }}>الجرد الدوري</h2>
        <button type="button" onClick={() => setShowForm((s) => !s)} style={btn('#00796b')}>
          {showForm ? 'إلغاء' : '+ خطة جرد'}
        </button>
      </div>
      {error ? <ErrorBanner message={error} /> : null}

      {showForm ? (
        <div style={card()}>
          <div style={grid()}>
            <label style={lbl()}>المستودع*
              <input value={form.warehouseId} onChange={(e) => setForm({ ...form, warehouseId: e.target.value })} style={inp()} placeholder="Warehouse ID" />
            </label>
            <label style={lbl()}>نطاق الجرد
              <select value={form.scopeType} onChange={(e) => setForm({ ...form, scopeType: e.target.value })} style={inp()}>
                <option value="FULL">كامل</option>
                <option value="LOCATION">حسب الموقع</option>
                <option value="CATEGORY">حسب الفئة</option>
                <option value="ABC">تحليل ABC</option>
              </select>
            </label>
            <label style={lbl()}>تاريخ التنفيذ
              <input type="date" value={form.scheduledFor} onChange={(e) => setForm({ ...form, scheduledFor: e.target.value })} style={inp()} />
            </label>
            <label style={lbl()}>مرشّح النطاق (JSON)
              <input value={form.scopeFilterJson ?? ''} onChange={(e) => setForm({ ...form, scopeFilterJson: e.target.value })} style={inp()} placeholder='{"locationId":"..."}' />
            </label>
          </div>
          <button type="button" disabled={busy === 'create'} onClick={() => void create()} style={btn('#004d40')}>حفظ الخطة</button>
        </div>
      ) : null}

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto', marginTop: '12px' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th>رقم الخطة</th><th>المستودع</th><th>النطاق</th><th>التاريخ</th><th>الحالة</th><th>إجراءات</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr><td colSpan={6} style={{ textAlign: 'center', padding: '16px', color: '#777' }}>لا توجد خطط جرد</td></tr>
            ) : rows.map((p) => (
              <>
                <tr key={p.id}>
                  <td>{p.planNo}</td>
                  <td>{p.warehouseId.slice(0, 8)}</td>
                  <td>{p.scopeType}</td>
                  <td>{p.scheduledFor ? new Date(p.scheduledFor).toLocaleDateString('ar') : '-'}</td>
                  <td>{p.status}</td>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    <button type="button" disabled={busy === p.id} onClick={() => setRecordFor((v) => (v === p.id ? '' : p.id))} style={btn('#00796b')}>تسجيل الجرد</button>
                    <button type="button" disabled={busy === p.id} onClick={() => void approveVariance(p.id)} style={btn('#004d40')}>اعتماد الفروقات</button>
                  </td>
                </tr>
                {recordFor === p.id ? (
                  <tr key={`${p.id}-record`}>
                    <td colSpan={6} style={{ background: '#f7fafa', padding: '12px' }}>
                      <div style={{ fontSize: '13px', color: '#333', marginBottom: '6px' }}>أسطر الجرد (JSON): [{'{'} lineId, countedQty {'}'}]</div>
                      <textarea value={recordJson} onChange={(e) => setRecordJson(e.target.value)} rows={5} style={{ ...inp(), width: '100%', fontFamily: 'monospace', direction: 'ltr' }} />
                      <div style={{ marginTop: '8px' }}>
                        <button type="button" disabled={busy === p.id} onClick={() => void submitRecord(p.id)} style={btn('#004d40')}>إرسال النتائج</button>
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
  );
}

function btn(bg: string): React.CSSProperties {
  return { border: 'none', borderRadius: '8px', background: bg, color: '#fff', padding: '6px 12px', margin: '0 4px', cursor: 'pointer', fontSize: '13px' };
}
function card(): React.CSSProperties {
  return { background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', padding: '16px', marginTop: '12px' };
}
function grid(): React.CSSProperties {
  return { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: '12px', marginBottom: '12px' };
}
function lbl(): React.CSSProperties {
  return { display: 'flex', flexDirection: 'column', gap: '4px', fontSize: '13px', color: '#333' };
}
function inp(): React.CSSProperties {
  return { padding: '8px', border: '1px solid #b0bec5', borderRadius: '8px' };
}

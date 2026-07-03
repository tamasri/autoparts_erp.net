import { useEffect, useState } from 'react';
import { fxRatesApi, type CreateFxRate } from '../../api/endpoints/fxRates';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

type FxRate = {
  id: string;
  rateDate?: string;
  buyRate?: number;
  sellRate?: number;
  midRate?: number;
};

const today = new Date().toISOString().slice(0, 10);
const emptyForm: CreateFxRate = { buyRate: 0, sellRate: 0, midRate: 0, rateDate: today };

function extractError(e: unknown, fallback: string): string {
  const r = e as { response?: { data?: { detail?: string; message?: string } } };
  return r.response?.data?.detail ?? r.response?.data?.message ?? fallback;
}

export default function FxRates(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<FxRate[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<CreateFxRate>(emptyForm);
  const [busy, setBusy] = useState(false);

  async function load(): Promise<void> {
    setLoading(true);
    setError('');
    try {
      const res = await fxRatesApi.getList(1, 30);
      setRows(unwrapList<FxRate>(res.data));
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر تحميل أسعار الصرف'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void load(); }, []);

  async function save(): Promise<void> {
    if (!form.rateDate || form.buyRate <= 0 || form.sellRate <= 0) {
      setError('التاريخ وأسعار الشراء والبيع مطلوبة');
      return;
    }
    setBusy(true);
    setError('');
    try {
      await fxRatesApi.create({
        ...form,
        midRate: form.midRate > 0 ? form.midRate : (form.buyRate + form.sellRate) / 2,
      });
      setForm(emptyForm);
      setShowForm(false);
      await load();
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر حفظ سعر الصرف'));
    } finally {
      setBusy(false);
    }
  }

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      {error ? <ErrorBanner message={error} /> : null}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
        <h2 style={{ margin: 0 }}>أسعار الصرف</h2>
        <button type="button" onClick={() => setShowForm((s) => !s)} style={btn('#00796b')}>
          {showForm ? 'إلغاء' : '+ سعر جديد'}
        </button>
      </div>

      {showForm ? (
        <div style={card()}>
          <div style={grid()}>
            <label style={lbl()}>التاريخ*
              <input type="date" value={form.rateDate} onChange={(e) => setForm({ ...form, rateDate: e.target.value })} style={inp()} />
            </label>
            <label style={lbl()}>سعر الشراء*
              <input type="number" value={form.buyRate} onChange={(e) => setForm({ ...form, buyRate: Number(e.target.value) })} style={inp()} />
            </label>
            <label style={lbl()}>سعر البيع*
              <input type="number" value={form.sellRate} onChange={(e) => setForm({ ...form, sellRate: Number(e.target.value) })} style={inp()} />
            </label>
            <label style={lbl()}>سعر الوسط
              <input type="number" value={form.midRate} onChange={(e) => setForm({ ...form, midRate: Number(e.target.value) })} style={inp()} placeholder="يُحسب تلقائياً" />
            </label>
          </div>
          <button type="button" disabled={busy} onClick={() => void save()} style={btn('#004d40')}>حفظ</button>
        </div>
      ) : null}

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto', marginTop: '12px' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th style={th()}>التاريخ</th>
              <th style={th()}>سعر الشراء</th>
              <th style={th()}>سعر البيع</th>
              <th style={th()}>سعر الوسط</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr><td colSpan={4} style={{ textAlign: 'center', padding: '16px', color: '#777' }}>لا توجد أسعار صرف</td></tr>
            ) : rows.map((r) => (
              <tr key={r.id}>
                <td style={td()}>{r.rateDate ?? '-'}</td>
                <td style={td()}>{Number(r.buyRate ?? 0).toLocaleString('en-US')}</td>
                <td style={td()}>{Number(r.sellRate ?? 0).toLocaleString('en-US')}</td>
                <td style={td()}>{Number(r.midRate ?? 0).toLocaleString('en-US')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function btn(bg: string): React.CSSProperties {
  return { border: 'none', borderRadius: '8px', background: bg, color: '#fff', padding: '8px 14px', cursor: 'pointer', fontSize: '13px' };
}
function card(): React.CSSProperties {
  return { background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', padding: '16px', marginTop: '12px' };
}
function grid(): React.CSSProperties {
  return { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))', gap: '12px', marginBottom: '12px' };
}
function lbl(): React.CSSProperties {
  return { display: 'flex', flexDirection: 'column', gap: '4px', fontSize: '13px', color: '#333' };
}
function inp(): React.CSSProperties {
  return { padding: '8px', border: '1px solid #b0bec5', borderRadius: '8px' };
}
function th(): React.CSSProperties {
  return { padding: '10px 12px', textAlign: 'right', background: '#f5f5f5', borderBottom: '1px solid #e0e0e0' };
}
function td(): React.CSSProperties {
  return { padding: '10px 12px', borderBottom: '1px solid #f0f0f0' };
}

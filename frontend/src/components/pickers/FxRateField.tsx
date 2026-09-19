import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { fxRatesApi } from '../../api/endpoints/fxRates';
import { unwrapList, unwrapNode } from '../../api/apiData';
import { toast, extractApiError } from '../../lib/toast';

export type FxRate = { id: string; rateDate: string; currencyFrom: string; currencyTo: string; buyRate: number; sellRate: number; midRate: number; isActive: boolean };

type Props = {
  /** Selected fx_rates id ('' until the latest rate has loaded). */
  value: string;
  onChange: (id: string, rate: FxRate | null) => void;
  /** Invoice date, used to warn when the selected rate is older than the document. */
  documentDate?: string;
};

const fmt = (v: number): string => Number(v ?? 0).toLocaleString('en-US');

/**
 * Exchange rate for a document. It is taken automatically from the latest rate saved in the system settings
 * (the FX Rates screen); the user can still switch to another saved rate or record a new one for this document.
 */
export default function FxRateField({ value, onChange, documentDate }: Props): JSX.Element {
  const [latest, setLatest] = useState<FxRate | null>(null);
  const [current, setCurrent] = useState<FxRate | null>(null);
  const [recent, setRecent] = useState<FxRate[]>([]);
  const [editing, setEditing] = useState(false);
  const [creating, setCreating] = useState(false);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState({ rateDate: new Date().toLocaleDateString('en-CA'), buyRate: 0, sellRate: 0, midRate: 0 });

  useEffect(() => {
    let live = true;
    fxRatesApi.getLatest()
      .then((res) => {
        if (!live) return;
        const rate = unwrapNode<FxRate>(res.data);
        setLatest(rate);
        if (rate) {
          setCurrent(rate);
          if (!value) onChange(rate.id, rate);
        }
      })
      .catch(() => { /* leave empty: the user is told below and can create a rate */ })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
    // Load once; `value` only matters for the initial default.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function openEdit(): Promise<void> {
    setEditing(true);
    try {
      const res = await fxRatesApi.getList(1, 30);
      setRecent(unwrapList<FxRate>(res.data));
    } catch { setRecent([]); }
  }

  function select(rate: FxRate): void {
    setCurrent(rate);
    onChange(rate.id, rate);
    setEditing(false);
    setCreating(false);
  }

  async function createRate(): Promise<void> {
    if (!(form.midRate > 0) || !(form.buyRate > 0) || !(form.sellRate > 0)) { toast.error('أدخل أسعار الشراء والبيع والوسط'); return; }
    setBusy(true);
    try {
      const res = await fxRatesApi.create(form);
      const created = unwrapNode<FxRate>(res.data);
      if (created) { toast.success('تم حفظ سعر الصرف'); select(created); }
    } catch (e: unknown) { toast.error(extractApiError(e, 'تعذر حفظ سعر الصرف')); }
    finally { setBusy(false); }
  }

  if (loading) return <div className="vex-input" style={{ color: 'var(--txt-muted)' }}>جارٍ تحميل سعر الصرف...</div>;

  if (!current) {
    return (
      <div className="vex-input" style={{ color: 'var(--clr-danger)' }}>
        لا يوجد سعر صرف في الإعدادات — <Link to="/fx-rates">أضف سعراً أولاً</Link>
      </div>
    );
  }

  const isLatest = latest?.id === current.id;
  const stale = documentDate && current.rateDate < documentDate;

  return (
    <div>
      <div className="vex-input" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, minHeight: 42 }}>
        <span>
          <strong><bdi dir="ltr">1 {current.currencyFrom} = {fmt(current.midRate)} {current.currencyTo}</bdi></strong>
          <span style={{ color: 'var(--txt-muted)', fontSize: 12 }}> · بيع <bdi dir="ltr">{fmt(current.sellRate)}</bdi> · <bdi dir="ltr">{current.rateDate}</bdi></span>
          <span className={`badge ${isLatest ? 'badge--success' : 'badge--warning'}`} style={{ marginInlineStart: 8 }}>{isLatest ? 'تلقائي' : 'معدّل'}</span>
        </span>
        <button type="button" className="btn-ghost" style={{ padding: '2px 10px', fontSize: 12 }} onClick={() => (editing ? setEditing(false) : void openEdit())}>
          {editing ? 'إغلاق' : 'تعديل'}
        </button>
      </div>
      {stale ? <div style={{ fontSize: 12, color: 'var(--clr-warning)', marginTop: 4 }}>⚠ سعر الصرف أقدم من تاريخ الفاتورة — حدّثه من الإعدادات إن لزم.</div> : null}

      {editing ? (
        <div className="vex-card" style={{ marginTop: 8, padding: 12 }}>
          <div style={{ fontSize: 12, color: 'var(--txt-secondary)', marginBottom: 6 }}>اختر سعراً محفوظاً:</div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4, maxHeight: 180, overflowY: 'auto' }}>
            {recent.map((r) => (
              <button key={r.id} type="button" className={r.id === current.id ? 'btn-primary' : 'btn-ghost'} style={{ textAlign: 'right', padding: '6px 10px', fontSize: 13 }} onClick={() => select(r)}>
                {r.rateDate} — {fmt(r.midRate)} {r.currencyTo}
              </button>
            ))}
          </div>
          <div style={{ marginTop: 10 }}>
            <button type="button" className="btn-ghost" style={{ fontSize: 13 }} onClick={() => setCreating((c) => !c)}>{creating ? '✕ إلغاء' : '＋ سعر جديد'}</button>
          </div>
          {creating ? (
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(130px, 1fr))', gap: 10, marginTop: 10 }}>
              <label className="vex-label">التاريخ<input type="date" className="vex-input" value={form.rateDate} onChange={(e) => setForm({ ...form, rateDate: e.target.value })} /></label>
              <label className="vex-label">شراء<input type="number" className="vex-input" value={form.buyRate} onChange={(e) => setForm({ ...form, buyRate: Number(e.target.value) })} /></label>
              <label className="vex-label">بيع<input type="number" className="vex-input" value={form.sellRate} onChange={(e) => setForm({ ...form, sellRate: Number(e.target.value) })} /></label>
              <label className="vex-label">وسط<input type="number" className="vex-input" value={form.midRate} onChange={(e) => setForm({ ...form, midRate: Number(e.target.value) })} /></label>
              <div style={{ alignSelf: 'end' }}><button type="button" className="btn-primary" disabled={busy} onClick={() => void createRate()}>{busy ? '...' : 'حفظ واعتماد'}</button></div>
            </div>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

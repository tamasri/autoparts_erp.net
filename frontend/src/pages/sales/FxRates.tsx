import { useEffect, useState } from 'react';
import { fxRatesApi, type CreateFxRate } from '../../api/endpoints/fxRates';
import { unwrapList } from '../../api/apiData';
import { toast, extractApiError } from '../../lib/toast';
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
      setError(extractApiError(e, 'تعذر تحميل أسعار الصرف'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void load(); }, []);

  async function save(): Promise<void> {
    if (!form.rateDate || form.buyRate <= 0 || form.sellRate <= 0) {
      toast.error('التاريخ وأسعار الشراء والبيع مطلوبة');
      return;
    }
    setBusy(true);
    try {
      await fxRatesApi.create({
        ...form,
        midRate: form.midRate > 0 ? form.midRate : (form.buyRate + form.sellRate) / 2,
      });
      toast.success('تم حفظ سعر الصرف بنجاح');
      setForm(emptyForm);
      setShowForm(false);
      await load();
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر حفظ سعر الصرف'));
    } finally {
      setBusy(false);
    }
  }

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      {/* Page Header */}
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">أسعار الصرف</h1>
          <div className="vex-page-header__breadcrumb">سعر الدولار مقابل الليرة السورية</div>
        </div>
        <button
          type="button"
          onClick={() => setShowForm((s) => !s)}
          className={showForm ? 'btn-ghost' : 'btn-primary'}
        >
          {showForm ? '✕ إلغاء' : '＋ سعر جديد'}
        </button>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {/* Create Form */}
      {showForm ? (
        <div className="vex-card" style={{ marginBottom: 20 }}>
          <h2 className="vex-section-title">إدخال سعر جديد</h2>
          <div style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))',
            gap: 16,
            marginBottom: 20,
          }}>
            <label className="vex-label">
              التاريخ *
              <input
                type="date"
                value={form.rateDate}
                onChange={(e) => setForm({ ...form, rateDate: e.target.value })}
                className="vex-input"
              />
            </label>
            <label className="vex-label">
              سعر الشراء *
              <input
                type="number"
                value={form.buyRate}
                onChange={(e) => setForm({ ...form, buyRate: Number(e.target.value) })}
                className="vex-input"
              />
            </label>
            <label className="vex-label">
              سعر البيع *
              <input
                type="number"
                value={form.sellRate}
                onChange={(e) => setForm({ ...form, sellRate: Number(e.target.value) })}
                className="vex-input"
              />
            </label>
            <label className="vex-label">
              سعر الوسط
              <input
                type="number"
                value={form.midRate}
                onChange={(e) => setForm({ ...form, midRate: Number(e.target.value) })}
                className="vex-input"
                placeholder="يُحسب تلقائياً"
              />
            </label>
          </div>

          {/* Mid-rate preview */}
          {form.buyRate > 0 && form.sellRate > 0 && (
            <div style={{
              padding: '12px 16px',
              background: 'var(--clr-primary-light)',
              borderRadius: 'var(--radius-md)',
              marginBottom: 16,
              fontSize: 13,
              color: 'var(--clr-primary-dark)',
            }}>
              سعر الوسط المحسوب: <strong>{((form.buyRate + form.sellRate) / 2).toLocaleString('en-US')}</strong> ل.س/$
            </div>
          )}

          <button
            type="button"
            disabled={busy}
            onClick={() => void save()}
            className="btn-primary"
          >
            {busy ? (
              <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <span className="vex-spinner" style={{ width: 16, height: 16, borderWidth: 2 }} />
                جارٍ الحفظ...
              </span>
            ) : '💾 حفظ'}
          </button>
        </div>
      ) : null}

      {/* Rates Table */}
      <div className="vex-card vex-card--no-pad">
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead>
              <tr>
                <th>التاريخ</th>
                <th>سعر الشراء</th>
                <th>سعر البيع</th>
                <th>سعر الوسط</th>
                <th>الفارق</th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 ? (
                <tr>
                  <td colSpan={5} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>
                    لا توجد أسعار صرف مسجّلة
                  </td>
                </tr>
              ) : rows.map((r, idx) => {
                const spread = Number(r.sellRate ?? 0) - Number(r.buyRate ?? 0);
                const isLatest = idx === 0;
                return (
                  <tr key={r.id}>
                    <td>
                      <span style={{ fontWeight: isLatest ? 700 : 400, color: 'var(--txt-primary)' }}>
                        {r.rateDate ?? '-'}
                      </span>
                      {isLatest && <span className="badge badge--primary" style={{ marginRight: 8, fontSize: 10 }}>أحدث</span>}
                    </td>
                    <td style={{ color: '#22c55e', fontWeight: 600 }}>
                      {Number(r.buyRate ?? 0).toLocaleString('en-US')}
                    </td>
                    <td style={{ color: 'var(--clr-danger)', fontWeight: 600 }}>
                      {Number(r.sellRate ?? 0).toLocaleString('en-US')}
                    </td>
                    <td style={{ fontWeight: 600, color: 'var(--txt-primary)' }}>
                      {Number(r.midRate ?? 0).toLocaleString('en-US')}
                    </td>
                    <td style={{ color: 'var(--txt-muted)', fontSize: 13 }}>
                      {spread.toLocaleString('en-US')}
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

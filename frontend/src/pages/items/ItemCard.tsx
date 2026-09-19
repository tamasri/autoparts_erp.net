import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { itemsApi, type PriceBody, type UpdateItemBody } from '../../api/endpoints/items';
import { unwrapList, unwrapNode } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';
import { toast, extractApiError } from '../../lib/toast';

type Item = {
  id: string; skuId?: string | null; partNumber: string; nameEn: string; nameAr: string;
  nameArColloquial?: string | null; brand?: string | null; categoryPath?: string | null;
  hasWarranty: boolean; warrantyMonths: number; isBatchTracked: boolean; reorderLevel: number;
  isActive: boolean; isStopShip: boolean; stopShipReason?: string | null; notes?: string | null;
};
type StockRow = { warehouse: string; availableQty: number; reservedQty: number };
type Alias = { id: string; alias: string; source: string; createdAt: string };
type Interchange = { id: string; interchangePartNumber: string; interchangeNameAr: string; type: string; priority: number; isActive: boolean };
type Sku = { sellingPriceSyp: number; sellingPriceUsd: number; minSellingPriceSyp: number; minSellingPriceUsd: number };
type Candidate = { id: string; partNumber: string; nameAr: string };

const TABS = [
  { key: 'overview', label: 'نظرة عامة' },
  { key: 'stock', label: 'المخزون' },
  { key: 'aliases', label: 'الأسماء البديلة' },
  { key: 'interchanges', label: 'المكافئات' },
  { key: 'prices', label: 'الأسعار' },
] as const;
type TabKey = (typeof TABS)[number]['key'];

const num = (v: number): string => Number(v ?? 0).toLocaleString('en-US');

export default function ItemCard(): JSX.Element {
  const { id = '' } = useParams();
  const [tab, setTab] = useState<TabKey>('overview');
  const [item, setItem] = useState<Item | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadItem = useCallback(async (): Promise<void> => {
    try {
      const res = await itemsApi.getById(id);
      setItem(unwrapNode<Item>(res.data));
      setError('');
    } catch (e: unknown) {
      setError(extractApiError(e, 'تعذر تحميل بطاقة الصنف'));
    } finally { setLoading(false); }
  }, [id]);

  useEffect(() => { setLoading(true); void loadItem(); }, [loadItem]);

  if (loading) return <LoadingSpinner />;
  if (!item) return <div style={{ direction: 'rtl' }}><ErrorBanner message={error || 'الصنف غير موجود'} /><Link to="/items">← العودة للأصناف</Link></div>;

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">{item.nameAr}</h1>
          <div className="vex-page-header__breadcrumb">
            <Link to="/items" style={{ color: 'inherit' }}>الأصناف</Link> / <span style={{ fontFamily: 'monospace' }}>{item.partNumber}</span> · {item.nameEn}
          </div>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          {!item.isActive ? <span className="badge badge--danger">غير نشط</span> : null}
          {item.isStopShip ? <span className="badge badge--warning">موقوف الشحن</span> : null}
        </div>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      <div style={{ display: 'flex', gap: 8, marginBottom: 16, flexWrap: 'wrap' }}>
        {TABS.map((t) => (
          <button
            key={t.key} type="button" onClick={() => setTab(t.key)}
            style={{
              border: 'none', borderRadius: 'var(--radius-pill)', padding: '7px 18px', cursor: 'pointer', fontSize: 13, fontWeight: 600, fontFamily: 'inherit',
              background: tab === t.key ? 'linear-gradient(135deg, var(--clr-primary), var(--clr-primary-mid))' : 'var(--clr-surface-2)',
              color: tab === t.key ? '#fff' : 'var(--txt-secondary)',
            }}
          >{t.label}</button>
        ))}
      </div>

      {tab === 'overview' ? <Overview item={item} onChanged={loadItem} /> : null}
      {tab === 'stock' ? <StockTab id={item.id} reorderLevel={item.reorderLevel} /> : null}
      {tab === 'aliases' ? <AliasesTab id={item.id} /> : null}
      {tab === 'interchanges' ? <InterchangesTab id={item.id} /> : null}
      {tab === 'prices' ? <PricesTab skuId={item.skuId ?? null} /> : null}
    </div>
  );
}

function Overview({ item, onChanged }: { item: Item; onChanged: () => Promise<void> }): JSX.Element {
  const [form, setForm] = useState<UpdateItemBody>({
    nameEn: item.nameEn, nameAr: item.nameAr, nameArColloquial: item.nameArColloquial ?? '', brand: item.brand ?? '',
    categoryPath: item.categoryPath ?? '', isActive: item.isActive, hasWarranty: item.hasWarranty, warrantyMonths: item.warrantyMonths,
    isBatchTracked: item.isBatchTracked, reorderLevel: item.reorderLevel, notes: item.notes ?? '',
  });
  const [busy, setBusy] = useState(false);
  const set = <K extends keyof UpdateItemBody>(k: K, v: UpdateItemBody[K]): void => setForm((f) => ({ ...f, [k]: v }));

  async function save(): Promise<void> {
    setBusy(true);
    try {
      await itemsApi.update(item.id, {
        ...form,
        nameArColloquial: form.nameArColloquial?.trim() || null,
        brand: form.brand?.trim() || null,
        categoryPath: form.categoryPath?.trim() || null,
        notes: form.notes?.trim() || null,
      });
      toast.success('تم حفظ التعديلات');
      await onChanged();
    } catch (e: unknown) { toast.error(extractApiError(e, 'تعذر حفظ التعديلات')); }
    finally { setBusy(false); }
  }

  async function stopShip(): Promise<void> {
    const reason = window.prompt('سبب إيقاف الشحن') ?? '';
    if (!reason.trim()) return;
    try { await itemsApi.stopShip(item.id, reason.trim()); toast.success('تم إيقاف شحن الصنف'); await onChanged(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر إيقاف الشحن')); }
  }

  return (
    <div className="vex-card">
      <h2 className="vex-section-title">بيانات الصنف</h2>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 16, marginBottom: 16 }}>
        <label className="vex-label">رقم القطعة<input className="vex-input" value={item.partNumber} disabled /></label>
        <label className="vex-label">الاسم (EN)<input className="vex-input" value={form.nameEn} onChange={(e) => set('nameEn', e.target.value)} /></label>
        <label className="vex-label">الاسم (AR)<input className="vex-input" value={form.nameAr} onChange={(e) => set('nameAr', e.target.value)} /></label>
        <label className="vex-label">الاسم الدارج<input className="vex-input" value={form.nameArColloquial ?? ''} onChange={(e) => set('nameArColloquial', e.target.value)} /></label>
        <label className="vex-label">العلامة التجارية<input className="vex-input" value={form.brand ?? ''} onChange={(e) => set('brand', e.target.value)} /></label>
        <label className="vex-label">التصنيف<input className="vex-input" value={form.categoryPath ?? ''} onChange={(e) => set('categoryPath', e.target.value)} /></label>
        <label className="vex-label">حد إعادة الطلب<input type="number" min={0} className="vex-input" value={form.reorderLevel} onChange={(e) => set('reorderLevel', Number(e.target.value))} /></label>
        <label className="vex-label">مدة الضمان (أشهر)<input type="number" min={0} className="vex-input" value={form.warrantyMonths} onChange={(e) => set('warrantyMonths', Number(e.target.value))} /></label>
      </div>
      <label className="vex-label" style={{ marginBottom: 16, display: 'block' }}>ملاحظات
        <textarea className="vex-input" rows={2} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value)} />
      </label>
      <div style={{ display: 'flex', gap: 20, marginBottom: 16, flexWrap: 'wrap' }}>
        <label><input type="checkbox" checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)} /> نشط</label>
        <label><input type="checkbox" checked={form.hasWarranty} onChange={(e) => set('hasWarranty', e.target.checked)} /> ضمان</label>
        <label><input type="checkbox" checked={form.isBatchTracked} onChange={(e) => set('isBatchTracked', e.target.checked)} /> تتبع بالدفعات</label>
      </div>
      {item.isStopShip ? <div className="badge badge--warning" style={{ marginBottom: 16 }}>موقوف الشحن: {item.stopShipReason ?? '-'}</div> : null}
      <div style={{ display: 'flex', gap: 10 }}>
        <button type="button" className="btn-primary" disabled={busy} onClick={() => void save()}>{busy ? 'جارٍ الحفظ...' : 'حفظ التعديلات'}</button>
        {!item.isStopShip ? <button type="button" className="btn-danger" onClick={() => void stopShip()}>🚫 إيقاف الشحن</button> : null}
      </div>
    </div>
  );
}

function useTabData<T>(loader: () => Promise<{ data: unknown }>, errorMessage: string): { rows: T[]; loading: boolean; error: string; reload: () => void } {
  const [rows, setRows] = useState<T[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [tick, setTick] = useState(0);
  useEffect(() => {
    let live = true;
    setLoading(true);
    loader().then((res) => { if (live) { setRows(unwrapList<T>(res.data)); setError(''); } })
      .catch((e: unknown) => { if (live) setError(extractApiError(e, errorMessage)); })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tick]);
  return { rows, loading, error, reload: () => setTick((t) => t + 1) };
}

function StockTab({ id, reorderLevel }: { id: string; reorderLevel: number }): JSX.Element {
  const { rows, loading, error } = useTabData<StockRow>(() => itemsApi.getStock(id), 'تعذر تحميل المخزون');
  const total = rows.reduce((s, r) => s + Number(r.availableQty), 0);
  return (
    <div className="vex-card vex-card--no-pad">
      {error ? <ErrorBanner message={error} /> : null}
      <table className="vex-table">
        <thead><tr><th>الموقع</th><th>المتوفر</th><th>المحجوز</th></tr></thead>
        <tbody>
          {loading ? <tr><td colSpan={3} style={{ textAlign: 'center', padding: 24 }}>جارٍ التحميل...</td></tr>
            : rows.length === 0 ? <tr><td colSpan={3} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: 24 }}>لا يوجد مخزون لهذا الصنف</td></tr>
              : rows.map((r) => (
                <tr key={r.warehouse}>
                  <td style={{ fontWeight: 600 }}>{r.warehouse}</td>
                  <td style={{ fontWeight: 700, color: Number(r.availableQty) > 0 ? '#22c55e' : 'var(--clr-danger)' }}>{num(r.availableQty)}</td>
                  <td>{num(r.reservedQty)}</td>
                </tr>
              ))}
        </tbody>
      </table>
      <div style={{ padding: '10px 18px', borderTop: '1px solid var(--clr-border)', fontSize: 13 }}>
        الإجمالي المتوفر: <strong>{num(total)}</strong> · حد إعادة الطلب: {num(reorderLevel)}
        {total <= reorderLevel ? <span className="badge badge--warning" style={{ marginRight: 8 }}>تحت حد الطلب</span> : null}
      </div>
    </div>
  );
}

function AliasesTab({ id }: { id: string }): JSX.Element {
  const { rows, loading, error, reload } = useTabData<Alias>(() => itemsApi.getAliases(id), 'تعذر تحميل الأسماء البديلة');
  const [alias, setAlias] = useState('');
  async function add(): Promise<void> {
    if (!alias.trim()) return;
    try { await itemsApi.addAlias(id, alias.trim()); setAlias(''); toast.success('تمت إضافة الاسم البديل'); reload(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر إضافة الاسم البديل')); }
  }
  return (
    <div className="vex-card">
      {error ? <ErrorBanner message={error} /> : null}
      <div style={{ display: 'flex', gap: 10, marginBottom: 16 }}>
        <input className="vex-input" style={{ maxWidth: 360 }} value={alias} onChange={(e) => setAlias(e.target.value)} placeholder="رقم أو اسم بديل للقطعة" />
        <button type="button" className="btn-primary" onClick={() => void add()}>＋ إضافة</button>
      </div>
      {loading ? 'جارٍ التحميل...' : rows.length === 0 ? <div style={{ color: 'var(--txt-muted)' }}>لا توجد أسماء بديلة</div> : (
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          {rows.map((a) => <span key={a.id} className="badge badge--draft" title={a.source}>{a.alias}</span>)}
        </div>
      )}
    </div>
  );
}

function InterchangesTab({ id }: { id: string }): JSX.Element {
  const { rows, loading, error, reload } = useTabData<Interchange>(() => itemsApi.getInterchanges(id), 'تعذر تحميل المكافئات');
  const [query, setQuery] = useState('');
  const [candidates, setCandidates] = useState<Candidate[]>([]);
  const [picked, setPicked] = useState<Candidate | null>(null);
  const [type, setType] = useState('EQUIVALENT');

  useEffect(() => {
    if (!query.trim() || picked) { setCandidates([]); return undefined; }
    const h = window.setTimeout(() => {
      itemsApi.search(query.trim()).then((r) => setCandidates(unwrapList<Candidate>(r.data).filter((c) => c.id !== id))).catch(() => setCandidates([]));
    }, 300);
    return () => window.clearTimeout(h);
  }, [query, picked, id]);

  async function add(): Promise<void> {
    if (!picked) return;
    try { await itemsApi.addInterchange(id, picked.id, type, 1); setPicked(null); setQuery(''); toast.success('تمت إضافة المكافئ'); reload(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر إضافة المكافئ')); }
  }

  return (
    <div className="vex-card">
      {error ? <ErrorBanner message={error} /> : null}
      <div style={{ display: 'flex', gap: 10, marginBottom: 8, flexWrap: 'wrap' }}>
        <input className="vex-input" style={{ maxWidth: 320 }} value={picked ? `${picked.partNumber} — ${picked.nameAr}` : query}
          onChange={(e) => { setPicked(null); setQuery(e.target.value); }} placeholder="ابحث عن صنف مكافئ..." />
        <select className="vex-input" style={{ width: 160 }} value={type} onChange={(e) => setType(e.target.value)}>
          <option value="EQUIVALENT">مكافئ</option><option value="SUPERSEDED">بديل أحدث</option><option value="COMPATIBLE">متوافق</option>
        </select>
        <button type="button" className="btn-primary" disabled={!picked} onClick={() => void add()}>＋ إضافة</button>
      </div>
      {candidates.length > 0 ? (
        <div style={{ border: '1px solid var(--clr-border)', borderRadius: 'var(--radius-sm)', marginBottom: 12, maxWidth: 480 }}>
          {candidates.map((c) => (
            <div key={c.id} onClick={() => setPicked(c)} style={{ padding: '8px 12px', cursor: 'pointer', borderBottom: '1px solid var(--clr-border)' }}>
              <span style={{ fontFamily: 'monospace', fontWeight: 700 }}>{c.partNumber}</span> — {c.nameAr}
            </div>
          ))}
        </div>
      ) : null}
      {loading ? 'جارٍ التحميل...' : rows.length === 0 ? <div style={{ color: 'var(--txt-muted)' }}>لا توجد مكافئات</div> : (
        <table className="vex-table">
          <thead><tr><th>رقم القطعة</th><th>الاسم</th><th>النوع</th><th>الأولوية</th></tr></thead>
          <tbody>{rows.map((r) => (
            <tr key={r.id}><td style={{ fontFamily: 'monospace', fontWeight: 700 }}>{r.interchangePartNumber}</td><td>{r.interchangeNameAr}</td><td>{r.type}</td><td>{r.priority}</td></tr>
          ))}</tbody>
        </table>
      )}
    </div>
  );
}

function PricesTab({ skuId }: { skuId: string | null }): JSX.Element {
  const [sku, setSku] = useState<Sku | null>(null);
  const [form, setForm] = useState<PriceBody | null>(null);
  const [loading, setLoading] = useState(Boolean(skuId));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!skuId) return;
    itemsApi.getSku(skuId).then((r) => {
      const s = unwrapNode<Sku>(r.data);
      setSku(s);
      if (s) setForm({ sellingPriceSyp: s.sellingPriceSyp, sellingPriceUsd: s.sellingPriceUsd, minSellingPriceSyp: s.minSellingPriceSyp, minSellingPriceUsd: s.minSellingPriceUsd });
    }).catch((e: unknown) => setError(extractApiError(e, 'تعذر تحميل الأسعار'))).finally(() => setLoading(false));
  }, [skuId]);

  if (!skuId) return <div className="vex-card" style={{ color: 'var(--txt-muted)' }}>هذا الصنف غير مرتبط بسجل تسعير (SKU).</div>;
  if (loading) return <LoadingSpinner />;
  if (!form || !sku) return <ErrorBanner message={error || 'تعذر تحميل الأسعار'} />;

  const set = (k: keyof PriceBody, v: number | string): void => setForm({ ...form, [k]: v });
  const belowMin = form.sellingPriceSyp < form.minSellingPriceSyp || form.sellingPriceUsd < form.minSellingPriceUsd;

  async function save(): Promise<void> {
    if (!form) return;
    if (belowMin && !form.overrideReason?.trim()) { toast.error('السعر أقل من الحد الأدنى: اكتب سبب التجاوز'); return; }
    setBusy(true);
    try { await itemsApi.updatePrices(skuId as string, form); toast.success('تم تحديث الأسعار'); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر تحديث الأسعار')); }
    finally { setBusy(false); }
  }

  return (
    <div className="vex-card">
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 16, marginBottom: 16 }}>
        <label className="vex-label">سعر البيع (ل.س)<input type="number" min={0} className="vex-input" value={form.sellingPriceSyp} onChange={(e) => set('sellingPriceSyp', Number(e.target.value))} /></label>
        <label className="vex-label">سعر البيع ($)<input type="number" min={0} className="vex-input" value={form.sellingPriceUsd} onChange={(e) => set('sellingPriceUsd', Number(e.target.value))} /></label>
        <label className="vex-label">الحد الأدنى (ل.س)<input type="number" min={0} className="vex-input" value={form.minSellingPriceSyp} onChange={(e) => set('minSellingPriceSyp', Number(e.target.value))} /></label>
        <label className="vex-label">الحد الأدنى ($)<input type="number" min={0} className="vex-input" value={form.minSellingPriceUsd} onChange={(e) => set('minSellingPriceUsd', Number(e.target.value))} /></label>
      </div>
      {belowMin ? (
        <label className="vex-label" style={{ display: 'block', marginBottom: 16 }}>سبب تجاوز الحد الأدنى *
          <input className="vex-input" value={form.overrideReason ?? ''} onChange={(e) => set('overrideReason', e.target.value)} />
        </label>
      ) : null}
      <button type="button" className="btn-primary" disabled={busy} onClick={() => void save()}>{busy ? 'جارٍ الحفظ...' : 'حفظ الأسعار'}</button>
    </div>
  );
}

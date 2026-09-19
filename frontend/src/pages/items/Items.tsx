import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { itemsApi, type CreateItemBody } from '../../api/endpoints/items';
import { usePagedList } from '../../hooks/usePagedList';
import Pagination from '../../components/common/Pagination';
import ErrorBanner from '../../components/common/ErrorBanner';
import { toast, extractApiError } from '../../lib/toast';

type ItemRow = {
  id: string;
  partNumber: string;
  nameEn: string;
  nameAr: string;
  brand?: string | null;
  isActive: boolean;
  isStopShip: boolean;
  hasWarranty: boolean;
  reorderLevel: number;
  availableQty: number;
};

const emptyForm: CreateItemBody = {
  partNumber: '', nameEn: '', nameAr: '', nameArColloquial: '', brand: '', categoryPath: '',
  hasWarranty: false, warrantyMonths: 0, isBatchTracked: false, reorderLevel: 0, notes: '',
};

export default function Items(): JSX.Element {
  const navigate = useNavigate();
  const [includeInactive, setIncludeInactive] = useState(false);
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<CreateItemBody>(emptyForm);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState('');

  const list = usePagedList<ItemRow>({
    errorMessage: 'تعذر تحميل الأصناف',
    deps: [includeInactive],
    fetcher: ({ page, pageSize, search }) => itemsApi.browse({ page, pageSize, search, includeInactive }),
  });

  async function create(): Promise<void> {
    if (!form.partNumber.trim() || !form.nameEn.trim() || !form.nameAr.trim()) {
      setFormError('رقم القطعة والاسم بالعربية والإنجليزية مطلوبة');
      return;
    }
    setBusy(true); setFormError('');
    try {
      const res = await itemsApi.create({
        ...form,
        partNumber: form.partNumber.trim(),
        nameArColloquial: form.nameArColloquial?.trim() || null,
        brand: form.brand?.trim() || null,
        categoryPath: form.categoryPath?.trim() || null,
        notes: form.notes?.trim() || null,
      });
      toast.success('تم إنشاء الصنف');
      setForm(emptyForm); setShowForm(false);
      const id = (res.data as { data?: { id?: string } })?.data?.id;
      if (id) navigate(`/items/${id}`); else list.reload();
    } catch (e: unknown) {
      setFormError(extractApiError(e, 'تعذر إنشاء الصنف'));
    } finally { setBusy(false); }
  }

  const set = <K extends keyof CreateItemBody>(k: K, v: CreateItemBody[K]): void => setForm((f) => ({ ...f, [k]: v }));

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">الأصناف</h1>
          <div className="vex-page-header__breadcrumb">بطاقات القطع: التفاصيل، المخزون، الأسماء البديلة، المكافئات والأسعار</div>
        </div>
        <button type="button" onClick={() => setShowForm((s) => !s)} className={showForm ? 'btn-ghost' : 'btn-primary'}>
          {showForm ? '✕ إلغاء' : '＋ صنف جديد'}
        </button>
      </div>

      {list.error ? <ErrorBanner message={list.error} /> : null}

      {showForm ? (
        <div className="vex-card" style={{ marginBottom: 20 }}>
          <h2 className="vex-section-title">إضافة صنف جديد</h2>
          {formError ? <ErrorBanner message={formError} /> : null}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 16, marginBottom: 16 }}>
            <label className="vex-label">رقم القطعة *<input className="vex-input" value={form.partNumber} onChange={(e) => set('partNumber', e.target.value)} /></label>
            <label className="vex-label">الاسم (EN) *<input className="vex-input" value={form.nameEn} onChange={(e) => set('nameEn', e.target.value)} /></label>
            <label className="vex-label">الاسم (AR) *<input className="vex-input" value={form.nameAr} onChange={(e) => set('nameAr', e.target.value)} /></label>
            <label className="vex-label">الاسم الدارج<input className="vex-input" value={form.nameArColloquial ?? ''} onChange={(e) => set('nameArColloquial', e.target.value)} /></label>
            <label className="vex-label">العلامة التجارية<input className="vex-input" value={form.brand ?? ''} onChange={(e) => set('brand', e.target.value)} /></label>
            <label className="vex-label">حد إعادة الطلب<input type="number" min={0} className="vex-input" value={form.reorderLevel} onChange={(e) => set('reorderLevel', Number(e.target.value))} /></label>
            <label className="vex-label">مدة الضمان (أشهر)<input type="number" min={0} className="vex-input" value={form.warrantyMonths} onChange={(e) => set('warrantyMonths', Number(e.target.value))} /></label>
          </div>
          <div style={{ display: 'flex', gap: 20, marginBottom: 16 }}>
            <label><input type="checkbox" checked={form.hasWarranty} onChange={(e) => set('hasWarranty', e.target.checked)} /> ضمان</label>
            <label><input type="checkbox" checked={form.isBatchTracked} onChange={(e) => set('isBatchTracked', e.target.checked)} /> تتبع بالدفعات</label>
          </div>
          <button type="button" className="btn-primary" disabled={busy} onClick={() => void create()}>{busy ? 'جارٍ الحفظ...' : 'حفظ الصنف'}</button>
        </div>
      ) : null}

      <div style={{ display: 'flex', gap: 16, alignItems: 'center', marginBottom: 16, flexWrap: 'wrap' }}>
        <input
          value={list.searchInput}
          onChange={(e) => list.setSearchInput(e.target.value)}
          placeholder="ابحث برقم القطعة أو الاسم أو العلامة التجارية..."
          className="vex-input"
          style={{ maxWidth: 460 }}
        />
        <label style={{ fontSize: 13, color: 'var(--txt-secondary)' }}>
          <input type="checkbox" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} /> عرض غير النشطة
        </label>
      </div>

      <div className="vex-card vex-card--no-pad" style={{ opacity: list.loading ? 0.6 : 1, transition: 'opacity 120ms' }}>
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead>
              <tr><th>رقم القطعة</th><th>الاسم</th><th>العلامة</th><th>المتوفر</th><th>حد الطلب</th><th>الحالة</th></tr>
            </thead>
            <tbody>
              {list.items.length === 0 ? (
                <tr><td colSpan={6} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '36px 0' }}>{list.loading ? 'جارٍ التحميل...' : 'لا توجد أصناف'}</td></tr>
              ) : list.items.map((row) => {
                const low = Number(row.availableQty) <= Number(row.reorderLevel);
                return (
                  <tr key={row.id} onClick={() => navigate(`/items/${row.id}`)} style={{ cursor: 'pointer' }}>
                    <td><span style={{ fontFamily: 'monospace', fontWeight: 700, color: 'var(--clr-primary)' }}>{row.partNumber}</span></td>
                    <td>
                      <div style={{ fontWeight: 600 }}>{row.nameAr}</div>
                      <div style={{ fontSize: 12, color: 'var(--txt-muted)', direction: 'ltr', textAlign: 'right' }}>{row.nameEn}</div>
                    </td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{row.brand ?? '-'}</td>
                    <td><span style={{ fontWeight: 700, color: Number(row.availableQty) > 0 ? '#22c55e' : 'var(--clr-danger)' }}>{Number(row.availableQty).toLocaleString('en-US')}</span></td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{Number(row.reorderLevel).toLocaleString('en-US')}</td>
                    <td>
                      {!row.isActive ? <span className="badge badge--danger">غير نشط</span>
                        : row.isStopShip ? <span className="badge badge--warning">موقوف الشحن</span>
                          : low ? <span className="badge badge--warning">تحت حد الطلب</span>
                            : <span className="badge badge--success">نشط</span>}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <Pagination page={list.page} pageSize={list.pageSize} totalCount={list.totalCount} onPageChange={list.setPage} onPageSizeChange={list.changePageSize} />
      </div>
    </div>
  );
}

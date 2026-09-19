import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { customersApi, type CreateCustomer, type UpdateCustomer } from '../../api/endpoints/customers';
import { unwrapList } from '../../api/apiData';
import { toast, extractApiError } from '../../lib/toast';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';
import StatusBadge from '../../components/common/StatusBadge';

type Customer = {
  id: string;
  code: string;
  name: string;
  type: string;
  phone?: string;
  phone2?: string;
  address?: string;
  city?: string;
  creditLimitSyp?: number;
  creditLimitUsd?: number;
  paymentTermsDays?: number;
  balanceSyp?: number;
  notes?: string;
  isActive?: boolean;
};

type FormState = {
  code: string;
  name: string;
  type: string;
  phone: string;
  phone2: string;
  address: string;
  city: string;
  creditLimitSyp: number;
  creditLimitUsd: number;
  paymentTermsDays: number;
  notes: string;
};

const emptyForm: FormState = {
  code: '', name: '', type: 'RETAIL', phone: '', phone2: '',
  address: '', city: '', creditLimitSyp: 0, creditLimitUsd: 0,
  paymentTermsDays: 0, notes: '',
};

const TYPE_STYLES: Record<string, { bg: string; color: string; label: string }> = {
  WORKSHOP: { bg: '#dbeafe', color: '#1d4ed8', label: 'ورشة' },
  RETAIL:   { bg: '#fef3c7', color: '#92400e', label: 'تجزئة' },
  WHOLESALE: { bg: '#f3e8ff', color: '#6b21a8', label: 'جملة' },
};

export default function Customers(): JSX.Element {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [rows, setRows] = useState<Customer[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [editId, setEditId] = useState<string | null>(null);
  const [form, setForm] = useState<FormState>(emptyForm);
  const [busy, setBusy] = useState(false);

  async function load(): Promise<void> {
    setLoading(true);
    setError('');
    try {
      const res = await customersApi.getCustomers({ page: 1, pageSize: 50 });
      setRows(unwrapList<Customer>(res.data));
    } catch (e: unknown) {
      setError(extractApiError(e, 'تعذر تحميل العملاء'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void load(); }, []);

  const filtered = useMemo(
    () => rows.filter((r) => r.name?.toLowerCase().includes(search.toLowerCase()) || r.code?.toLowerCase().includes(search.toLowerCase())),
    [rows, search],
  );

  function openCreate(): void {
    setEditId(null);
    setForm(emptyForm);
    setShowForm(true);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  function openEdit(c: Customer, e: React.MouseEvent): void {
    e.stopPropagation();
    setEditId(c.id);
    setForm({
      code: c.code ?? '', name: c.name ?? '', type: c.type ?? 'RETAIL',
      phone: c.phone ?? '', phone2: c.phone2 ?? '',
      address: c.address ?? '', city: c.city ?? '',
      creditLimitSyp: Number(c.creditLimitSyp ?? 0),
      creditLimitUsd: Number(c.creditLimitUsd ?? 0),
      paymentTermsDays: Number(c.paymentTermsDays ?? 0),
      notes: c.notes ?? '',
    });
    setShowForm(true);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  async function save(): Promise<void> {
    if (!form.name.trim() || (!editId && !form.code.trim())) {
      setError('الاسم والكود مطلوبان');
      return;
    }
    setBusy(true);
    setError('');
    try {
      if (editId) {
        const payload: UpdateCustomer = {
          name: form.name.trim(), type: form.type,
          phone: form.phone.trim() || undefined, phone2: form.phone2.trim() || undefined,
          address: form.address.trim() || undefined, city: form.city.trim() || undefined,
          creditLimitSyp: Number(form.creditLimitSyp), creditLimitUsd: Number(form.creditLimitUsd),
          paymentTermsDays: Number(form.paymentTermsDays), notes: form.notes.trim() || undefined,
        };
        await customersApi.updateCustomer(editId, payload);
      } else {
        const payload: CreateCustomer = {
          code: form.code.trim(), name: form.name.trim(), type: form.type,
          phone: form.phone.trim() || undefined, phone2: form.phone2.trim() || undefined,
          address: form.address.trim() || undefined, city: form.city.trim() || undefined,
          creditLimitSyp: Number(form.creditLimitSyp), creditLimitUsd: Number(form.creditLimitUsd),
          paymentTermsDays: Number(form.paymentTermsDays), notes: form.notes.trim() || undefined,
        };
        await customersApi.createCustomer(payload);
      }
      toast.success(editId ? 'تم تحديث العميل بنجاح' : 'تم إنشاء العميل بنجاح');
      setShowForm(false);
      setEditId(null);
      await load();
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر حفظ العميل'));
      setError(extractApiError(e, 'تعذر حفظ العميل'));
    } finally {
      setBusy(false);
    }
  }

  async function deactivate(c: Customer, e: React.MouseEvent): Promise<void> {
    e.stopPropagation();
    const reason = window.prompt('سبب إلغاء التفعيل:') ?? '';
    if (!reason.trim()) return;
    setBusy(true);
    try {
      await customersApi.deactivateCustomer(c.id, reason.trim());
      toast.success('تم إلغاء تفعيل العميل');
      await load();
    } catch (err: unknown) {
      toast.error(extractApiError(err, 'تعذر إلغاء تفعيل العميل'));
      setError(extractApiError(err, 'تعذر إلغاء تفعيل العميل'));
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
          <h1 className="vex-page-header__title">العملاء</h1>
          <div className="vex-page-header__breadcrumb">إدارة قاعدة بيانات العملاء</div>
        </div>
        <button type="button" onClick={openCreate} className="btn-primary">
          ＋ عميل جديد
        </button>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {/* Create / Edit Form */}
      {showForm ? (
        <div className="vex-card" style={{ marginBottom: 20 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
            <h2 className="vex-section-title" style={{ margin: 0 }}>
              {editId ? '✏️ تعديل عميل' : '＋ عميل جديد'}
            </h2>
            <button type="button" onClick={() => { setShowForm(false); setEditId(null); }} className="btn-ghost">
              ✕ إغلاق
            </button>
          </div>

          <div style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))',
            gap: 16,
            marginBottom: 20,
          }}>
            <label className="vex-label">
              الكود *
              <input
                value={form.code}
                disabled={Boolean(editId)}
                onChange={(e) => setForm({ ...form, code: e.target.value })}
                className="vex-input"
                placeholder="مثال: C001"
                style={editId ? { opacity: 0.6 } : undefined}
              />
            </label>
            <label className="vex-label">
              الاسم *
              <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} className="vex-input" />
            </label>
            <label className="vex-label">
              النوع
              <select value={form.type} onChange={(e) => setForm({ ...form, type: e.target.value })} className="vex-select">
                <option value="RETAIL">تجزئة</option>
                <option value="WHOLESALE">جملة</option>
                <option value="WORKSHOP">ورشة</option>
              </select>
            </label>
            <label className="vex-label">
              الهاتف
              <input value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} className="vex-input" />
            </label>
            <label className="vex-label">
              هاتف 2
              <input value={form.phone2} onChange={(e) => setForm({ ...form, phone2: e.target.value })} className="vex-input" />
            </label>
            <label className="vex-label">
              المدينة
              <input value={form.city} onChange={(e) => setForm({ ...form, city: e.target.value })} className="vex-input" />
            </label>
            <label className="vex-label">
              العنوان
              <input value={form.address} onChange={(e) => setForm({ ...form, address: e.target.value })} className="vex-input" />
            </label>
            <label className="vex-label">
              الحد الائتماني ل.س
              <input type="number" value={form.creditLimitSyp} onChange={(e) => setForm({ ...form, creditLimitSyp: Number(e.target.value) })} className="vex-input" />
            </label>
            <label className="vex-label">
              الحد الائتماني $
              <input type="number" value={form.creditLimitUsd} onChange={(e) => setForm({ ...form, creditLimitUsd: Number(e.target.value) })} className="vex-input" />
            </label>
            <label className="vex-label">
              شروط الدفع (أيام)
              <input type="number" value={form.paymentTermsDays} onChange={(e) => setForm({ ...form, paymentTermsDays: Number(e.target.value) })} className="vex-input" />
            </label>
            <label className="vex-label">
              ملاحظات
              <input value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} className="vex-input" />
            </label>
          </div>

          <div style={{ display: 'flex', gap: 10 }}>
            <button type="button" disabled={busy} onClick={() => void save()} className="btn-primary">
              {busy ? 'جارٍ الحفظ...' : '💾 حفظ'}
            </button>
            <button type="button" onClick={() => { setShowForm(false); setEditId(null); }} className="btn-ghost">
              إلغاء
            </button>
          </div>
        </div>
      ) : null}

      {/* Search */}
      <div style={{ marginBottom: 16, position: 'relative' }}>
        <span style={{ position: 'absolute', top: '50%', right: 14, transform: 'translateY(-50%)', color: 'var(--txt-muted)', pointerEvents: 'none', fontSize: 16 }}>🔍</span>
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="بحث بالاسم أو الكود..."
          className="vex-input"
          style={{ paddingRight: 40 }}
        />
      </div>

      {/* Customers Table */}
      <div className="vex-card vex-card--no-pad">
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead>
              <tr>
                <th>الكود</th>
                <th>الاسم</th>
                <th>النوع</th>
                <th>المدينة</th>
                <th>الرصيد المتأخر</th>
                <th>الحد الائتماني</th>
                <th>الحالة</th>
                <th>إجراءات</th>
              </tr>
            </thead>
            <tbody>
              {filtered.length === 0 ? (
                <tr>
                  <td colSpan={8} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '36px 0' }}>
                    {search ? 'لا توجد نتائج مطابقة' : 'لا يوجد عملاء'}
                  </td>
                </tr>
              ) : filtered.map((row) => {
                const typeStyle = TYPE_STYLES[row.type.toUpperCase()] ?? { bg: '#f1f5f9', color: '#475569', label: row.type };
                return (
                  <tr key={row.id} onClick={() => navigate(`/customers/${row.id}`)} style={{ cursor: 'pointer' }}>
                    <td>
                      <span style={{
                        background: 'var(--clr-primary-light)', color: 'var(--clr-primary-dark)',
                        padding: '2px 8px', borderRadius: 'var(--radius-sm)', fontSize: 12, fontWeight: 700,
                      }}>{row.code}</span>
                    </td>
                    <td style={{ fontWeight: 600, color: 'var(--txt-primary)' }}>{row.name}</td>
                    <td>
                      <span style={{
                        padding: '3px 10px', borderRadius: 'var(--radius-pill)',
                        background: typeStyle.bg, color: typeStyle.color, fontSize: 12, fontWeight: 700,
                      }}>{typeStyle.label}</span>
                    </td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{row.city ?? '-'}</td>
                    <td style={{ fontWeight: 600, color: Number(row.balanceSyp ?? 0) > 0 ? 'var(--clr-danger)' : 'var(--txt-primary)' }}>
                      {Number(row.balanceSyp ?? 0).toLocaleString('en-US')}
                    </td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{Number(row.creditLimitSyp ?? 0).toLocaleString('en-US')}</td>
                    <td><StatusBadge status={row.isActive !== false ? 'ACTIVE' : 'INACTIVE'} type="customer" /></td>
                    <td style={{ whiteSpace: 'nowrap' }} onClick={(e) => e.stopPropagation()}>
                      <button type="button" onClick={(e) => openEdit(row, e)} className="btn-secondary" style={{ padding: '5px 12px', fontSize: 12, marginLeft: 6 }}>
                        تعديل
                      </button>
                      {row.isActive !== false ? (
                        <button type="button" disabled={busy} onClick={(e) => void deactivate(row, e)} className="btn-danger" style={{ padding: '5px 12px', fontSize: 12 }}>
                          إلغاء التفعيل
                        </button>
                      ) : null}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <div style={{ padding: '10px 18px', borderTop: '1px solid var(--clr-border)', fontSize: 12, color: 'var(--txt-muted)' }}>
          إجمالي النتائج: {filtered.length} من {rows.length} عميل
        </div>
      </div>
    </div>
  );
}

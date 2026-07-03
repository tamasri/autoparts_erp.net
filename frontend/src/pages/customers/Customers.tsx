import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { customersApi, type CreateCustomer, type UpdateCustomer } from '../../api/endpoints/customers';
import { unwrapList } from '../../api/apiData';
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

function extractError(e: unknown, fallback: string): string {
  const r = e as { response?: { data?: { detail?: string; message?: string } } };
  return r.response?.data?.detail ?? r.response?.data?.message ?? fallback;
}

function typeBadgeColor(type: string): string {
  const normalized = type.toUpperCase();
  if (normalized === 'WORKSHOP') return '#1976d2';
  if (normalized === 'RETAIL') return '#ef6c00';
  if (normalized === 'WHOLESALE') return '#7b1fa2';
  return '#607d8b';
}

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
  code: '',
  name: '',
  type: 'RETAIL',
  phone: '',
  phone2: '',
  address: '',
  city: '',
  creditLimitSyp: 0,
  creditLimitUsd: 0,
  paymentTermsDays: 0,
  notes: '',
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
      setError(extractError(e, 'تعذر تحميل العملاء'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  const filtered = useMemo(
    () => rows.filter((r) => r.name?.toLowerCase().includes(search.toLowerCase())),
    [rows, search],
  );

  function openCreate(): void {
    setEditId(null);
    setForm(emptyForm);
    setShowForm(true);
  }

  function openEdit(c: Customer, e: React.MouseEvent): void {
    e.stopPropagation();
    setEditId(c.id);
    setForm({
      code: c.code ?? '',
      name: c.name ?? '',
      type: c.type ?? 'RETAIL',
      phone: c.phone ?? '',
      phone2: c.phone2 ?? '',
      address: c.address ?? '',
      city: c.city ?? '',
      creditLimitSyp: Number(c.creditLimitSyp ?? 0),
      creditLimitUsd: Number(c.creditLimitUsd ?? 0),
      paymentTermsDays: Number(c.paymentTermsDays ?? 0),
      notes: c.notes ?? '',
    });
    setShowForm(true);
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
          name: form.name.trim(),
          type: form.type,
          phone: form.phone.trim() || undefined,
          phone2: form.phone2.trim() || undefined,
          address: form.address.trim() || undefined,
          city: form.city.trim() || undefined,
          creditLimitSyp: Number(form.creditLimitSyp),
          creditLimitUsd: Number(form.creditLimitUsd),
          paymentTermsDays: Number(form.paymentTermsDays),
          notes: form.notes.trim() || undefined,
        };
        await customersApi.updateCustomer(editId, payload);
      } else {
        const payload: CreateCustomer = {
          code: form.code.trim(),
          name: form.name.trim(),
          type: form.type,
          phone: form.phone.trim() || undefined,
          phone2: form.phone2.trim() || undefined,
          address: form.address.trim() || undefined,
          city: form.city.trim() || undefined,
          creditLimitSyp: Number(form.creditLimitSyp),
          creditLimitUsd: Number(form.creditLimitUsd),
          paymentTermsDays: Number(form.paymentTermsDays),
          notes: form.notes.trim() || undefined,
        };
        await customersApi.createCustomer(payload);
      }
      setShowForm(false);
      await load();
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر حفظ العميل'));
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
      await load();
    } catch (err: unknown) {
      setError(extractError(err, 'تعذر إلغاء تفعيل العميل'));
    } finally {
      setBusy(false);
    }
  }

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      {error ? <ErrorBanner message={error} /> : null}
      <div style={{ display: 'flex', gap: '10px', justifyContent: 'space-between', marginBottom: '10px' }}>
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="بحث بالاسم..."
          style={{ flex: 1, border: '1px solid #cfd8dc', borderRadius: '8px', padding: '8px' }}
        />
        <button type="button" onClick={openCreate} style={btn('#00796b')}>عميل جديد</button>
      </div>

      {showForm ? (
        <div style={card()}>
          <h3 style={{ marginTop: 0 }}>{editId ? 'تعديل عميل' : 'عميل جديد'}</h3>
          <div style={grid()}>
            <label style={lbl()}>الكود*
              <input value={form.code} disabled={Boolean(editId)} onChange={(e) => setForm({ ...form, code: e.target.value })} style={inp()} />
            </label>
            <label style={lbl()}>الاسم*
              <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} style={inp()} />
            </label>
            <label style={lbl()}>النوع
              <select value={form.type} onChange={(e) => setForm({ ...form, type: e.target.value })} style={inp()}>
                <option value="RETAIL">تجزئة</option>
                <option value="WHOLESALE">جملة</option>
                <option value="WORKSHOP">ورشة</option>
              </select>
            </label>
            <label style={lbl()}>الهاتف
              <input value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} style={inp()} />
            </label>
            <label style={lbl()}>هاتف 2
              <input value={form.phone2} onChange={(e) => setForm({ ...form, phone2: e.target.value })} style={inp()} />
            </label>
            <label style={lbl()}>المدينة
              <input value={form.city} onChange={(e) => setForm({ ...form, city: e.target.value })} style={inp()} />
            </label>
            <label style={lbl()}>العنوان
              <input value={form.address} onChange={(e) => setForm({ ...form, address: e.target.value })} style={inp()} />
            </label>
            <label style={lbl()}>الحد الائتماني ل.س
              <input type="number" value={form.creditLimitSyp} onChange={(e) => setForm({ ...form, creditLimitSyp: Number(e.target.value) })} style={inp()} />
            </label>
            <label style={lbl()}>الحد الائتماني $
              <input type="number" value={form.creditLimitUsd} onChange={(e) => setForm({ ...form, creditLimitUsd: Number(e.target.value) })} style={inp()} />
            </label>
            <label style={lbl()}>شروط الدفع (أيام)
              <input type="number" value={form.paymentTermsDays} onChange={(e) => setForm({ ...form, paymentTermsDays: Number(e.target.value) })} style={inp()} />
            </label>
            <label style={lbl()}>ملاحظات
              <input value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} style={inp()} />
            </label>
          </div>
          <div style={{ display: 'flex', gap: '8px' }}>
            <button type="button" disabled={busy} onClick={() => void save()} style={btn('#004d40')}>حفظ</button>
            <button type="button" onClick={() => setShowForm(false)} style={btn('#607d8b')}>إلغاء</button>
          </div>
        </div>
      ) : null}

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto', marginTop: '12px' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
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
            {filtered.map((row) => (
              <tr key={row.id} onClick={() => navigate(`/customers/${row.id}`)} style={{ cursor: 'pointer' }}>
                <td>{row.code}</td>
                <td>{row.name}</td>
                <td>
                  <span style={{ padding: '2px 8px', borderRadius: '999px', background: `${typeBadgeColor(row.type)}22`, color: typeBadgeColor(row.type), fontSize: '12px', fontWeight: 700 }}>
                    {row.type}
                  </span>
                </td>
                <td>{row.city ?? '-'}</td>
                <td>{Number(row.balanceSyp ?? 0).toLocaleString('en-US')}</td>
                <td>{Number(row.creditLimitSyp ?? 0).toLocaleString('en-US')}</td>
                <td><StatusBadge status={row.isActive ? 'ACTIVE' : 'INACTIVE'} type="customer" /></td>
                <td style={{ whiteSpace: 'nowrap' }}>
                  <button type="button" onClick={(e) => openEdit(row, e)} style={btn('#1565c0')}>تعديل</button>
                  {row.isActive !== false ? (
                    <button type="button" disabled={busy} onClick={(e) => void deactivate(row, e)} style={btn('#c62828')}>إلغاء التفعيل</button>
                  ) : null}
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
  return { border: 'none', borderRadius: '8px', background: bg, color: '#fff', padding: '6px 12px', margin: '0 4px', cursor: 'pointer', fontSize: '13px' };
}
function card(): React.CSSProperties {
  return { background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', padding: '16px', marginTop: '12px' };
}
function grid(): React.CSSProperties {
  return { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: '12px', marginBottom: '12px' };
}
function lbl(): React.CSSProperties {
  return { display: 'flex', flexDirection: 'column', gap: '4px', fontSize: '13px', color: '#333' };
}
function inp(): React.CSSProperties {
  return { padding: '8px', border: '1px solid #b0bec5', borderRadius: '8px' };
}

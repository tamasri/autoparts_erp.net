import { useEffect, useState } from 'react';
import { receivingApi, type CreateReceivingDocument } from '../../api/endpoints/receiving';
import { unwrapList } from '../../api/apiData';
import { toast, extractApiError } from '../../lib/toast';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';
import StatusBadge from '../../components/common/StatusBadge';

type ReceivingDoc = {
  id: string;
  documentNo: string;
  vendorPartyId?: string;
  purchaseOrderRef?: string;
  warehouseId: string;
  status: string;
  receivedAt?: string;
  postedAt?: string;
  notes?: string;
};

type PutawayTask = {
  id: string;
  receivingLineId: string;
  fromLocationId: string;
  toLocationId: string;
  qty: number;
  status: string;
};

const emptyForm: CreateReceivingDocument = { warehouseId: '', vendorPartyId: '', purchaseOrderRef: '', notes: '' };

export default function Receiving(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<ReceivingDoc[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<CreateReceivingDocument>(emptyForm);
  const [busy, setBusy] = useState('');
  const [tasks, setTasks] = useState<Record<string, PutawayTask[]>>({});
  const [expandedId, setExpandedId] = useState<string | null>(null);

  async function load(): Promise<void> {
    setLoading(true);
    setError('');
    try {
      const res = await receivingApi.list(1, 100);
      setRows(unwrapList<ReceivingDoc>(res.data));
    } catch (e: unknown) {
      setError(extractApiError(e, 'تعذر تحميل مستندات الاستلام'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void load(); }, []);

  async function create(): Promise<void> {
    if (!form.warehouseId.trim()) { setError('معرّف المستودع مطلوب'); return; }
    setBusy('create');
    try {
      await receivingApi.create({
        warehouseId: form.warehouseId.trim(),
        vendorPartyId: form.vendorPartyId?.trim() || undefined,
        purchaseOrderRef: form.purchaseOrderRef?.trim() || undefined,
        notes: form.notes?.trim() || undefined,
      });
      setForm(emptyForm);
      setShowForm(false);
      toast.success('تم إنشاء مستند الاستلام');
      await load();
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر إنشاء المستند'));
      setError(extractApiError(e, 'تعذر إنشاء المستند'));
    } finally { setBusy(''); }
  }

  async function post(id: string): Promise<void> {
    setBusy(id);
    try {
      await receivingApi.post(id);
      toast.success('تم ترحيل مستند الاستلام');
      await load();
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر ترحيل المستند'));
      setError(extractApiError(e, 'تعذر ترحيل المستند'));
    } finally { setBusy(''); }
  }

  async function loadTasks(id: string): Promise<void> {
    setBusy(id);
    try {
      const res = await receivingApi.getPutawayTasks(id);
      setTasks((prev) => ({ ...prev, [id]: unwrapList<PutawayTask>(res.data) }));
      setExpandedId((prev) => (prev === id ? null : id));
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر تحميل مهام التخزين'));
      setError(extractApiError(e, 'تعذر تحميل مهام التخزين'));
    } finally { setBusy(''); }
  }

  async function completeTask(docId: string, task: PutawayTask): Promise<void> {
    const to = window.prompt('معرّف موقع التخزين (Location ID):', task.toLocationId) ?? '';
    if (!to.trim()) return;
    setBusy(task.id);
    try {
      await receivingApi.completePutaway(task.id, { toLocationId: to.trim(), qty: task.qty });
      await loadTasks(docId);
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر إتمام مهمة التخزين'));
      setError(extractApiError(e, 'تعذر إتمام مهمة التخزين'));
    } finally { setBusy(''); }
  }

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">الاستلام والتخزين</h1>
          <div className="vex-page-header__breadcrumb">استلام البضاعة من الموردين وتخزينها</div>
        </div>
        <button type="button" onClick={() => setShowForm((s) => !s)} className={showForm ? 'btn-ghost' : 'btn-primary'}>
          {showForm ? '✕ إلغاء' : '＋ مستند استلام'}
        </button>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {showForm ? (
        <div className="vex-card" style={{ marginBottom: 20 }}>
          <h2 className="vex-section-title">مستند استلام جديد</h2>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 16, marginBottom: 20 }}>
            <label className="vex-label">
              المستودع *
              <input value={form.warehouseId} onChange={(e) => setForm({ ...form, warehouseId: e.target.value })} className="vex-input" placeholder="Warehouse ID" />
            </label>
            <label className="vex-label">
              المورّد
              <input value={form.vendorPartyId} onChange={(e) => setForm({ ...form, vendorPartyId: e.target.value })} className="vex-input" placeholder="Vendor Party ID" />
            </label>
            <label className="vex-label">
              مرجع أمر الشراء
              <input value={form.purchaseOrderRef} onChange={(e) => setForm({ ...form, purchaseOrderRef: e.target.value })} className="vex-input" />
            </label>
            <label className="vex-label">
              ملاحظات
              <input value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} className="vex-input" />
            </label>
          </div>
          <button type="button" disabled={busy === 'create'} onClick={() => void create()} className="btn-primary">
            💾 حفظ المستند
          </button>
        </div>
      ) : null}

      <div className="vex-card vex-card--no-pad">
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead>
              <tr>
                <th>رقم المستند</th>
                <th>المستودع</th>
                <th>الحالة</th>
                <th>تاريخ الترحيل</th>
                <th>إجراءات</th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 ? (
                <tr><td colSpan={5} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>لا توجد مستندات</td></tr>
              ) : rows.map((d) => (
                <>
                  <tr key={d.id}>
                    <td style={{ fontWeight: 600, color: 'var(--clr-primary)' }}>{d.documentNo}</td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{d.warehouseId.slice(0, 8)}</td>
                    <td><StatusBadge status={d.status} type="invoice" /></td>
                    <td style={{ color: 'var(--txt-secondary)' }}>
                      {d.postedAt ? new Date(d.postedAt).toLocaleDateString('ar') : '-'}
                    </td>
                    <td style={{ whiteSpace: 'nowrap' }}>
                      {d.status !== 'POSTED' ? (
                        <button type="button" disabled={busy === d.id} onClick={() => void post(d.id)} className="btn-success" style={{ padding: '5px 14px', fontSize: 12, marginLeft: 6 }}>
                          ✓ ترحيل
                        </button>
                      ) : null}
                      <button type="button" disabled={busy === d.id} onClick={() => void loadTasks(d.id)} className="btn-secondary" style={{ padding: '5px 14px', fontSize: 12 }}>
                        {expandedId === d.id ? '▲ إخفاء' : '▼ مهام التخزين'}
                      </button>
                    </td>
                  </tr>
                  {expandedId === d.id && tasks[d.id] ? (
                    <tr key={`${d.id}-tasks`}>
                      <td colSpan={5} style={{ background: 'var(--clr-surface-2)', padding: '12px 20px' }}>
                        <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--txt-muted)', marginBottom: 8, textTransform: 'uppercase' }}>
                          مهام التخزين
                        </div>
                        {tasks[d.id].length === 0 ? (
                          <div style={{ color: 'var(--txt-muted)', fontSize: 13 }}>لا توجد مهام</div>
                        ) : tasks[d.id].map((t) => (
                          <div key={t.id} style={{
                            display: 'flex', gap: 14, alignItems: 'center',
                            padding: '8px 12px', marginBottom: 6,
                            background: '#fff', borderRadius: 'var(--radius-md)',
                            border: '1px solid var(--clr-border)',
                          }}>
                            <span style={{ fontSize: 13, color: 'var(--txt-secondary)' }}>الكمية: <strong>{t.qty}</strong></span>
                            <StatusBadge status={t.status} type="invoice" />
                            {t.status !== 'COMPLETED' ? (
                              <button type="button" disabled={busy === t.id} onClick={() => void completeTask(d.id, t)} className="btn-primary" style={{ padding: '4px 12px', fontSize: 12 }}>
                                إتمام
                              </button>
                            ) : null}
                          </div>
                        ))}
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

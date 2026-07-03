import { useEffect, useState } from 'react';
import { receivingApi, type CreateReceivingDocument } from '../../api/endpoints/receiving';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

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

function extractError(e: unknown, fallback: string): string {
  const r = e as { response?: { data?: { detail?: string; message?: string } } };
  return r.response?.data?.detail ?? r.response?.data?.message ?? fallback;
}

const emptyForm: CreateReceivingDocument = { warehouseId: '', vendorPartyId: '', purchaseOrderRef: '', notes: '' };

export default function Receiving(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<ReceivingDoc[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<CreateReceivingDocument>(emptyForm);
  const [busy, setBusy] = useState('');
  const [tasks, setTasks] = useState<Record<string, PutawayTask[]>>({});

  async function load(): Promise<void> {
    setLoading(true);
    setError('');
    try {
      const res = await receivingApi.list(1, 100);
      setRows(unwrapList<ReceivingDoc>(res.data));
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر تحميل مستندات الاستلام'));
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
      const payload: CreateReceivingDocument = {
        warehouseId: form.warehouseId.trim(),
        vendorPartyId: form.vendorPartyId?.trim() || undefined,
        purchaseOrderRef: form.purchaseOrderRef?.trim() || undefined,
        notes: form.notes?.trim() || undefined,
      };
      await receivingApi.create(payload);
      setForm(emptyForm);
      setShowForm(false);
      await load();
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر إنشاء المستند'));
    } finally {
      setBusy('');
    }
  }

  async function post(id: string): Promise<void> {
    setBusy(id);
    try {
      await receivingApi.post(id);
      await load();
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر ترحيل المستند'));
    } finally {
      setBusy('');
    }
  }

  async function loadTasks(id: string): Promise<void> {
    setBusy(id);
    try {
      const res = await receivingApi.getPutawayTasks(id);
      setTasks((prev) => ({ ...prev, [id]: unwrapList<PutawayTask>(res.data) }));
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر تحميل مهام التخزين'));
    } finally {
      setBusy('');
    }
  }

  async function completeTask(docId: string, task: PutawayTask): Promise<void> {
    const to = window.prompt('معرّف موقع التخزين (Location ID):', task.toLocationId) ?? '';
    if (!to.trim()) return;
    setBusy(task.id);
    try {
      await receivingApi.completePutaway(task.id, { toLocationId: to.trim(), qty: task.qty });
      await loadTasks(docId);
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر إتمام مهمة التخزين'));
    } finally {
      setBusy('');
    }
  }

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <h2 style={{ marginTop: 0 }}>الاستلام والتخزين</h2>
        <button type="button" onClick={() => setShowForm((s) => !s)} style={btn('#00796b')}>
          {showForm ? 'إلغاء' : '+ مستند استلام'}
        </button>
      </div>
      {error ? <ErrorBanner message={error} /> : null}

      {showForm ? (
        <div style={card()}>
          <div style={grid()}>
            <label style={lbl()}>المستودع*
              <input value={form.warehouseId} onChange={(e) => setForm({ ...form, warehouseId: e.target.value })} style={inp()} placeholder="Warehouse ID" />
            </label>
            <label style={lbl()}>المورّد
              <input value={form.vendorPartyId} onChange={(e) => setForm({ ...form, vendorPartyId: e.target.value })} style={inp()} placeholder="Vendor Party ID" />
            </label>
            <label style={lbl()}>مرجع أمر الشراء
              <input value={form.purchaseOrderRef} onChange={(e) => setForm({ ...form, purchaseOrderRef: e.target.value })} style={inp()} />
            </label>
            <label style={lbl()}>ملاحظات
              <input value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} style={inp()} />
            </label>
          </div>
          <button type="button" disabled={busy === 'create'} onClick={() => void create()} style={btn('#004d40')}>حفظ المستند</button>
        </div>
      ) : null}

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto', marginTop: '12px' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th>رقم المستند</th><th>المستودع</th><th>الحالة</th><th>تاريخ الترحيل</th><th>إجراءات</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr><td colSpan={5} style={{ textAlign: 'center', padding: '16px', color: '#777' }}>لا توجد مستندات</td></tr>
            ) : rows.map((d) => (
              <>
                <tr key={d.id}>
                  <td>{d.documentNo}</td>
                  <td>{d.warehouseId.slice(0, 8)}</td>
                  <td>{d.status}</td>
                  <td>{d.postedAt ? new Date(d.postedAt).toLocaleDateString('ar') : '-'}</td>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    {d.status !== 'POSTED' ? (
                      <button type="button" disabled={busy === d.id} onClick={() => void post(d.id)} style={btn('#00796b')}>ترحيل</button>
                    ) : (
                      <button type="button" disabled={busy === d.id} onClick={() => void loadTasks(d.id)} style={btn('#00695c')}>مهام التخزين</button>
                    )}
                  </td>
                </tr>
                {tasks[d.id]?.length ? (
                  <tr key={`${d.id}-tasks`}>
                    <td colSpan={5} style={{ background: '#f7fafa', padding: '8px' }}>
                      {tasks[d.id].map((t) => (
                        <div key={t.id} style={{ display: 'flex', gap: '12px', alignItems: 'center', padding: '4px 0' }}>
                          <span>كمية: {t.qty}</span>
                          <span>الحالة: {t.status}</span>
                          {t.status !== 'COMPLETED' ? (
                            <button type="button" disabled={busy === t.id} onClick={() => void completeTask(d.id, t)} style={btn('#004d40')}>إتمام</button>
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

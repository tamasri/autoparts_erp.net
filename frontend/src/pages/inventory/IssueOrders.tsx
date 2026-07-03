import { useEffect, useState } from 'react';
import {
  issueOrdersApi,
  type CreateIssueOrder,
  type IssueOrderLine,
} from '../../api/endpoints/issueOrders';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

type IssueOrder = {
  id: string;
  orderNo: string;
  sourceType: string;
  warehouseId: string;
  status: string;
  issuedAt?: string;
};

type PickTask = {
  id: string;
  itemId: string;
  locationId?: string;
  qty: number;
  status: string;
};

function extractError(e: unknown, fallback: string): string {
  const r = e as { response?: { data?: { detail?: string; message?: string } } };
  return r.response?.data?.detail ?? r.response?.data?.message ?? fallback;
}

function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) return crypto.randomUUID();
  return `io-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

const emptyLine: IssueOrderLine = {
  itemId: '',
  requestedQty: 0,
  sourceLocationId: '',
};

export default function IssueOrders(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<IssueOrder[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState('');
  const [sourceType, setSourceType] = useState('MANUAL');
  const [sourceId, setSourceId] = useState('');
  const [warehouseId, setWarehouseId] = useState('');
  const [lines, setLines] = useState<IssueOrderLine[]>([{ ...emptyLine }]);
  const [tasks, setTasks] = useState<Record<string, PickTask[]>>({});

  async function load(): Promise<void> {
    setLoading(true);
    setError('');
    try {
      const res = await issueOrdersApi.list(1, 100);
      setRows(unwrapList<IssueOrder>(res.data));
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر تحميل أوامر الصرف'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  function updateLine(idx: number, patch: Partial<IssueOrderLine>): void {
    setLines((prev) => prev.map((l, i) => (i === idx ? { ...l, ...patch } : l)));
  }

  function addLine(): void {
    setLines((prev) => [...prev, { ...emptyLine }]);
  }

  function removeLine(idx: number): void {
    setLines((prev) => (prev.length > 1 ? prev.filter((_, i) => i !== idx) : prev));
  }

  async function create(): Promise<void> {
    if (!warehouseId.trim()) {
      setError('معرّف المستودع مطلوب');
      return;
    }
    const cleanLines = lines
      .filter((l) => l.itemId.trim() && l.requestedQty > 0)
      .map((l) => ({
        itemId: l.itemId.trim(),
        requestedQty: Number(l.requestedQty),
        sourceLocationId: l.sourceLocationId?.trim() || undefined,
      }));
    if (cleanLines.length === 0) {
      setError('أضف سطراً واحداً على الأقل بكمية صحيحة');
      return;
    }
    setBusy('create');
    try {
      const payload: CreateIssueOrder = {
        sourceType,
        sourceId: sourceId.trim() || undefined,
        warehouseId: warehouseId.trim(),
        lines: cleanLines,
        idempotencyKey: newIdempotencyKey(),
      };
      await issueOrdersApi.create(payload);
      setSourceId('');
      setWarehouseId('');
      setLines([{ ...emptyLine }]);
      setShowForm(false);
      await load();
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر إنشاء أمر الصرف'));
    } finally {
      setBusy('');
    }
  }

  async function generateTasks(id: string): Promise<void> {
    setBusy(id);
    try {
      const res = await issueOrdersApi.generatePickTasks(id);
      setTasks((prev) => ({ ...prev, [id]: unwrapList<PickTask>(res.data) }));
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر توليد مهام السحب'));
    } finally {
      setBusy('');
    }
  }

  async function completePick(orderId: string, taskId: string): Promise<void> {
    setBusy(taskId);
    try {
      await issueOrdersApi.completePick(orderId, taskId);
      await generateTasks(orderId);
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر إتمام مهمة السحب'));
    } finally {
      setBusy('');
    }
  }

  async function verifyPick(orderId: string, taskId: string): Promise<void> {
    setBusy(taskId);
    try {
      await issueOrdersApi.verifyPick(orderId, taskId);
      await generateTasks(orderId);
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر التحقق من مهمة السحب'));
    } finally {
      setBusy('');
    }
  }

  async function issue(id: string): Promise<void> {
    setBusy(id);
    try {
      await issueOrdersApi.issue(id);
      await load();
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر صرف الأمر'));
    } finally {
      setBusy('');
    }
  }

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <h2 style={{ marginTop: 0 }}>أوامر الصرف</h2>
        <button type="button" onClick={() => setShowForm((s) => !s)} style={btn('#00796b')}>
          {showForm ? 'إلغاء' : '+ أمر صرف'}
        </button>
      </div>
      {error ? <ErrorBanner message={error} /> : null}

      {showForm ? (
        <div style={card()}>
          <div style={grid()}>
            <label style={lbl()}>نوع المصدر
              <select value={sourceType} onChange={(e) => setSourceType(e.target.value)} style={inp()}>
                <option value="MANUAL">يدوي</option>
                <option value="SALES_ORDER">أمر بيع</option>
                <option value="TRANSFER">تحويل</option>
              </select>
            </label>
            <label style={lbl()}>معرّف المصدر
              <input value={sourceId} onChange={(e) => setSourceId(e.target.value)} style={inp()} placeholder="Source ID (optional)" />
            </label>
            <label style={lbl()}>المستودع*
              <input value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} style={inp()} placeholder="Warehouse ID" />
            </label>
          </div>

          <div style={{ fontSize: '13px', fontWeight: 700, margin: '8px 0', color: '#00695c' }}>الأصناف</div>
          {lines.map((l, idx) => (
            <div key={idx} style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(160px, 1fr)) 40px', gap: '8px', marginBottom: '8px', alignItems: 'end' }}>
              <label style={lbl()}>الصنف
                <input value={l.itemId} onChange={(e) => updateLine(idx, { itemId: e.target.value })} style={inp()} placeholder="Item ID" />
              </label>
              <label style={lbl()}>الكمية المطلوبة
                <input type="number" value={l.requestedQty} onChange={(e) => updateLine(idx, { requestedQty: Number(e.target.value) })} style={inp()} />
              </label>
              <label style={lbl()}>موقع المصدر
                <input value={l.sourceLocationId} onChange={(e) => updateLine(idx, { sourceLocationId: e.target.value })} style={inp()} placeholder="From Location" />
              </label>
              <button type="button" onClick={() => removeLine(idx)} style={btn('#c62828')}>×</button>
            </div>
          ))}
          <button type="button" onClick={addLine} style={btn('#455a64')}>+ سطر</button>
          <div style={{ marginTop: '12px' }}>
            <button type="button" disabled={busy === 'create'} onClick={() => void create()} style={btn('#004d40')}>حفظ الأمر</button>
          </div>
        </div>
      ) : null}

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto', marginTop: '12px' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th>رقم الأمر</th><th>المصدر</th><th>المستودع</th><th>الحالة</th><th>إجراءات</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr><td colSpan={5} style={{ textAlign: 'center', padding: '16px', color: '#777' }}>لا توجد أوامر صرف</td></tr>
            ) : rows.map((o) => (
              <>
                <tr key={o.id}>
                  <td>{o.orderNo}</td>
                  <td>{o.sourceType}</td>
                  <td>{o.warehouseId.slice(0, 8)}</td>
                  <td>{o.status}</td>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    <button type="button" disabled={busy === o.id} onClick={() => void generateTasks(o.id)} style={btn('#00796b')}>مهام السحب</button>
                    {o.status !== 'ISSUED' ? (
                      <button type="button" disabled={busy === o.id} onClick={() => void issue(o.id)} style={btn('#004d40')}>صرف</button>
                    ) : <span style={{ color: '#2e7d32' }}>مصروف</span>}
                  </td>
                </tr>
                {tasks[o.id]?.length ? (
                  <tr key={`${o.id}-tasks`}>
                    <td colSpan={5} style={{ background: '#f7fafa', padding: '8px' }}>
                      {tasks[o.id].map((t) => (
                        <div key={t.id} style={{ display: 'flex', gap: '12px', alignItems: 'center', padding: '4px 0' }}>
                          <span>صنف: {t.itemId.slice(0, 8)}</span>
                          <span>كمية: {t.qty}</span>
                          <span>الحالة: {t.status}</span>
                          {t.status === 'PENDING' ? (
                            <button type="button" disabled={busy === t.id} onClick={() => void completePick(o.id, t.id)} style={btn('#00796b')}>إتمام السحب</button>
                          ) : null}
                          {t.status === 'PICKED' ? (
                            <button type="button" disabled={busy === t.id} onClick={() => void verifyPick(o.id, t.id)} style={btn('#004d40')}>تحقق</button>
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

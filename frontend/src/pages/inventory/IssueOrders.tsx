import { useEffect, useState } from 'react';
import { issueOrdersApi, type CreateIssueOrder, type IssueOrderLine } from '../../api/endpoints/issueOrders';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';
import StatusBadge from '../../components/common/StatusBadge';

type IssueOrder = { id: string; orderNo: string; sourceType: string; warehouseId: string; status: string; issuedAt?: string };
type PickTask = { id: string; itemId: string; locationId?: string; qty: number; status: string };

function extractError(e: unknown, fallback: string): string {
  const r = e as { response?: { data?: { detail?: string; message?: string } } };
  return r.response?.data?.detail ?? r.response?.data?.message ?? fallback;
}

function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) return crypto.randomUUID();
  return `io-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

const emptyLine: IssueOrderLine = { itemId: '', requestedQty: 0, sourceLocationId: '' };

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
  const [expandedId, setExpandedId] = useState<string | null>(null);

  async function load(): Promise<void> {
    setLoading(true); setError('');
    try { const res = await issueOrdersApi.list(1, 100); setRows(unwrapList<IssueOrder>(res.data)); }
    catch (e: unknown) { setError(extractError(e, 'تعذر تحميل أوامر الصرف')); }
    finally { setLoading(false); }
  }

  useEffect(() => { void load(); }, []);

  function updateLine(idx: number, patch: Partial<IssueOrderLine>): void {
    setLines((prev) => prev.map((l, i) => (i === idx ? { ...l, ...patch } : l)));
  }

  async function create(): Promise<void> {
    if (!warehouseId.trim()) { setError('معرّف المستودع مطلوب'); return; }
    const cleanLines = lines.filter((l) => l.itemId.trim() && l.requestedQty > 0).map((l) => ({
      itemId: l.itemId.trim(), requestedQty: Number(l.requestedQty),
      sourceLocationId: l.sourceLocationId?.trim() || undefined,
    }));
    if (cleanLines.length === 0) { setError('أضف سطراً واحداً على الأقل بكمية صحيحة'); return; }
    setBusy('create');
    try {
      await issueOrdersApi.create({ sourceType, sourceId: sourceId.trim() || undefined, warehouseId: warehouseId.trim(), lines: cleanLines, idempotencyKey: newIdempotencyKey() } as CreateIssueOrder);
      setSourceId(''); setWarehouseId(''); setLines([{ ...emptyLine }]); setShowForm(false); await load();
    } catch (e: unknown) { setError(extractError(e, 'تعذر إنشاء أمر الصرف')); }
    finally { setBusy(''); }
  }

  async function generateTasks(id: string): Promise<void> {
    setBusy(id);
    try {
      const res = await issueOrdersApi.generatePickTasks(id);
      setTasks((prev) => ({ ...prev, [id]: unwrapList<PickTask>(res.data) }));
      setExpandedId((prev) => (prev === id ? null : id));
    } catch (e: unknown) { setError(extractError(e, 'تعذر توليد مهام السحب')); }
    finally { setBusy(''); }
  }

  async function completePick(orderId: string, taskId: string): Promise<void> {
    setBusy(taskId);
    try { await issueOrdersApi.completePick(orderId, taskId); await generateTasks(orderId); }
    catch (e: unknown) { setError(extractError(e, 'تعذر إتمام مهمة السحب')); }
    finally { setBusy(''); }
  }

  async function verifyPick(orderId: string, taskId: string): Promise<void> {
    setBusy(taskId);
    try { await issueOrdersApi.verifyPick(orderId, taskId); await generateTasks(orderId); }
    catch (e: unknown) { setError(extractError(e, 'تعذر التحقق من مهمة السحب')); }
    finally { setBusy(''); }
  }

  async function issue(id: string): Promise<void> {
    setBusy(id);
    try { await issueOrdersApi.issue(id); await load(); }
    catch (e: unknown) { setError(extractError(e, 'تعذر صرف الأمر')); }
    finally { setBusy(''); }
  }

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">أوامر الصرف</h1>
          <div className="vex-page-header__breadcrumb">إنشاء وإدارة أوامر صرف المواد من المخزون</div>
        </div>
        <button type="button" onClick={() => setShowForm((s) => !s)} className={showForm ? 'btn-ghost' : 'btn-primary'}>
          {showForm ? '✕ إلغاء' : '＋ أمر صرف'}
        </button>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {showForm ? (
        <div className="vex-card" style={{ marginBottom: 20 }}>
          <h2 className="vex-section-title">أمر صرف جديد</h2>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 16, marginBottom: 20 }}>
            <label className="vex-label">
              نوع المصدر
              <select value={sourceType} onChange={(e) => setSourceType(e.target.value)} className="vex-select">
                <option value="MANUAL">يدوي</option>
                <option value="SALES_ORDER">أمر بيع</option>
                <option value="TRANSFER">تحويل</option>
              </select>
            </label>
            <label className="vex-label">
              معرّف المصدر
              <input value={sourceId} onChange={(e) => setSourceId(e.target.value)} className="vex-input" placeholder="Source ID (optional)" />
            </label>
            <label className="vex-label">
              المستودع *
              <input value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} className="vex-input" placeholder="Warehouse ID" />
            </label>
          </div>

          <h3 className="vex-section-title" style={{ marginBottom: 12 }}>الأصناف</h3>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10, marginBottom: 14 }}>
            {lines.map((l, idx) => (
              <div key={idx} style={{ background: 'var(--clr-surface-2)', border: '1px solid var(--clr-border)', borderRadius: 'var(--radius-md)', padding: 14, position: 'relative' }}>
                <div style={{ position: 'absolute', top: 10, left: 10, width: 22, height: 22, background: 'var(--clr-primary-light)', color: 'var(--clr-primary)', borderRadius: '50%', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 11, fontWeight: 700 }}>{idx + 1}</div>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(160px, 1fr))', gap: 12, paddingLeft: 32 }}>
                  <label className="vex-label">الصنف <input value={l.itemId} onChange={(e) => updateLine(idx, { itemId: e.target.value })} className="vex-input" placeholder="Item ID" /></label>
                  <label className="vex-label">الكمية المطلوبة <input type="number" value={l.requestedQty} onChange={(e) => updateLine(idx, { requestedQty: Number(e.target.value) })} className="vex-input" /></label>
                  <label className="vex-label">موقع المصدر <input value={l.sourceLocationId} onChange={(e) => updateLine(idx, { sourceLocationId: e.target.value })} className="vex-input" /></label>
                </div>
                <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 10 }}>
                  <button type="button" onClick={() => setLines((prev) => prev.length > 1 ? prev.filter((_, i) => i !== idx) : prev)} className="btn-danger" style={{ padding: '4px 12px', fontSize: 12 }}>✕ حذف</button>
                </div>
              </div>
            ))}
          </div>
          <div style={{ display: 'flex', gap: 10 }}>
            <button type="button" onClick={() => setLines((prev) => [...prev, { ...emptyLine }])} className="btn-secondary">＋ إضافة سطر</button>
            <button type="button" disabled={busy === 'create'} onClick={() => void create()} className="btn-primary">💾 حفظ الأمر</button>
          </div>
        </div>
      ) : null}

      <div className="vex-card vex-card--no-pad">
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead>
              <tr>
                <th>رقم الأمر</th>
                <th>المصدر</th>
                <th>المستودع</th>
                <th>الحالة</th>
                <th>إجراءات</th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 ? (
                <tr><td colSpan={5} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>لا توجد أوامر صرف</td></tr>
              ) : rows.map((o) => (
                <>
                  <tr key={o.id}>
                    <td style={{ fontWeight: 600, color: 'var(--clr-primary)' }}>{o.orderNo}</td>
                    <td><span style={{ fontSize: 12, color: 'var(--txt-muted)', background: 'var(--clr-surface-2)', padding: '2px 8px', borderRadius: 'var(--radius-sm)' }}>{o.sourceType}</span></td>
                    <td><span className="badge badge--draft">{o.warehouseId.slice(0, 8)}</span></td>
                    <td><StatusBadge status={o.status} type="invoice" /></td>
                    <td style={{ whiteSpace: 'nowrap' }}>
                      <button type="button" disabled={busy === o.id} onClick={() => void generateTasks(o.id)} className="btn-secondary" style={{ padding: '5px 12px', fontSize: 12, marginLeft: 6 }}>
                        {expandedId === o.id ? '▲ إخفاء' : '📋 مهام السحب'}
                      </button>
                      {o.status !== 'ISSUED' ? (
                        <button type="button" disabled={busy === o.id} onClick={() => void issue(o.id)} className="btn-primary" style={{ padding: '5px 12px', fontSize: 12 }}>
                          ✓ صرف
                        </button>
                      ) : <span className="badge badge--success">مصروف</span>}
                    </td>
                  </tr>
                  {expandedId === o.id && tasks[o.id] ? (
                    <tr key={`${o.id}-tasks`}>
                      <td colSpan={5} style={{ background: 'var(--clr-surface-2)', padding: '12px 20px' }}>
                        <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--txt-muted)', marginBottom: 8, textTransform: 'uppercase' }}>مهام السحب</div>
                        {tasks[o.id].map((t) => (
                          <div key={t.id} style={{ display: 'flex', gap: 14, alignItems: 'center', padding: '8px 12px', marginBottom: 6, background: '#fff', borderRadius: 'var(--radius-md)', border: '1px solid var(--clr-border)' }}>
                            <span style={{ fontSize: 12, color: 'var(--txt-muted)', fontFamily: 'monospace' }}>{t.itemId.slice(0, 10)}</span>
                            <span style={{ fontSize: 13, color: 'var(--txt-secondary)' }}>كمية: <strong>{t.qty}</strong></span>
                            <StatusBadge status={t.status} type="invoice" />
                            {t.status === 'PENDING' ? (
                              <button type="button" disabled={busy === t.id} onClick={() => void completePick(o.id, t.id)} className="btn-primary" style={{ padding: '4px 12px', fontSize: 12 }}>إتمام</button>
                            ) : null}
                            {t.status === 'PICKED' ? (
                              <button type="button" disabled={busy === t.id} onClick={() => void verifyPick(o.id, t.id)} className="btn-success" style={{ padding: '4px 12px', fontSize: 12 }}>تحقق</button>
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

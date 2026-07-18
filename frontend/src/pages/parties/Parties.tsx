import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { partiesApi } from '../../api/endpoints/parties';
import { unwrapList } from '../../api/apiData';
import { toast, extractApiError } from '../../lib/toast';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

type PartyType = { typeCode?: string; code?: string; isActive?: boolean };
type Party = {
  id: string;
  code?: string;
  displayName?: string;
  city?: string;
  isActive?: boolean;
  typeAssignments?: PartyType[];
  types?: PartyType[];
};

function extractError(e: unknown, fallback: string): string {
  return extractApiError(e, fallback);
}

const TYPE_OPTIONS = ['CUSTOMER', 'VENDOR', 'SALES_REP', 'CARRIER'];

export default function Parties(): JSX.Element {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<Party[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState(false);
  const [displayName, setDisplayName] = useState('');
  const [displayNameAr, setDisplayNameAr] = useState('');
  const [taxNumber, setTaxNumber] = useState('');
  const [notes, setNotes] = useState('');
  const [selectedTypes, setSelectedTypes] = useState<string[]>(['CUSTOMER']);

  async function load(): Promise<void> {
    setLoading(true);
    setError('');
    try {
      const res = await partiesApi.getParties({ page: 1, pageSize: 50 });
      setRows(unwrapList<Party>(res.data));
    } catch (e: unknown) {
      setError(extractError(e, 'تعذر تحميل الأطراف'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  function toggleType(t: string): void {
    setSelectedTypes((prev) => (prev.includes(t) ? prev.filter((x) => x !== t) : [...prev, t]));
  }

  async function create(): Promise<void> {
    if (!displayName.trim() || !displayNameAr.trim()) {
      setError('الاسم بالعربية والإنجليزية مطلوبان');
      return;
    }
    setBusy(true);
    setError('');
    try {
      await partiesApi.createParty({
        displayName: displayName.trim(),
        displayNameAr: displayNameAr.trim(),
        taxNumber: taxNumber.trim() || undefined,
        notes: notes.trim() || undefined,
        initialTypeCodes: selectedTypes.length > 0 ? selectedTypes : undefined,
      });
      setDisplayName('');
      setDisplayNameAr('');
      setTaxNumber('');
      setNotes('');
      setSelectedTypes(['CUSTOMER']);
      setShowForm(false);
      toast.success('تم إنشاء الطرف بنجاح');
      await load();
    } catch (e: unknown) {
      toast.error(extractApiError(e, 'تعذر إنشاء الطرف'));
      setError(extractApiError(e, 'تعذر إنشاء الطرف'));
    } finally {
      setBusy(false);
    }
  }

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      {error ? <ErrorBanner message={error} /> : null}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '10px' }}>
        <h2 style={{ margin: 0 }}>الأطراف</h2>
        <button type="button" onClick={() => setShowForm((s) => !s)} style={btn('#00796b')}>{showForm ? 'إلغاء' : '+ طرف جديد'}</button>
      </div>

      {showForm ? (
        <div style={card()}>
          <div style={grid()}>
            <label style={lbl()}>الاسم (EN)*
              <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} style={inp()} />
            </label>
            <label style={lbl()}>الاسم (AR)*
              <input value={displayNameAr} onChange={(e) => setDisplayNameAr(e.target.value)} style={inp()} />
            </label>
            <label style={lbl()}>الرقم الضريبي
              <input value={taxNumber} onChange={(e) => setTaxNumber(e.target.value)} style={inp()} />
            </label>
            <label style={lbl()}>ملاحظات
              <input value={notes} onChange={(e) => setNotes(e.target.value)} style={inp()} />
            </label>
          </div>
          <div style={{ marginBottom: '12px' }}>
            <div style={{ fontSize: '13px', color: '#333', marginBottom: '6px' }}>الأنواع الأولية</div>
            <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
              {TYPE_OPTIONS.map((t) => (
                <button
                  key={t}
                  type="button"
                  onClick={() => toggleType(t)}
                  style={{
                    border: '1px solid #b0bec5',
                    borderRadius: '999px',
                    padding: '4px 12px',
                    cursor: 'pointer',
                    background: selectedTypes.includes(t) ? '#00796b' : '#fff',
                    color: selectedTypes.includes(t) ? '#fff' : '#37474f',
                    fontSize: '12px',
                  }}
                >
                  {t}
                </button>
              ))}
            </div>
          </div>
          <button type="button" disabled={busy} onClick={() => void create()} style={btn('#004d40')}>حفظ الطرف</button>
        </div>
      ) : null}

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto', marginTop: '12px' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th>الكود</th>
              <th>الاسم</th>
              <th>الأنواع</th>
              <th>المدينة</th>
              <th>الحالة</th>
              <th>إجراءات</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => {
              const typeList = (row.typeAssignments ?? row.types ?? []).map((t) => (t.typeCode ?? t.code ?? '').toUpperCase()).filter(Boolean);
              const dual = typeList.includes('CUSTOMER') && typeList.includes('VENDOR');
              return (
                <tr key={row.id}>
                  <td>{row.code ?? '-'}</td>
                  <td>{row.displayName ?? '-'}</td>
                  <td>
                    <div style={{ display: 'flex', gap: '4px', flexWrap: 'wrap' }}>
                      {typeList.map((type) => (
                        <span key={`${row.id}-${type}`} style={{ background: '#eceff1', borderRadius: '999px', padding: '2px 8px', fontSize: '12px' }}>
                          {type}
                        </span>
                      ))}
                      {dual ? <span style={{ background: '#fff8e1', color: '#e65100', borderRadius: '999px', padding: '2px 8px', fontSize: '12px' }}>عميل+مورد</span> : null}
                    </div>
                  </td>
                  <td>{row.city ?? '-'}</td>
                  <td>{row.isActive ? 'نشط' : 'غير نشط'}</td>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    <button type="button" onClick={() => navigate(`/parties/${row.id}/statement`)} style={btn('#1565c0')}>كشف مدمج</button>
                  </td>
                </tr>
              );
            })}
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

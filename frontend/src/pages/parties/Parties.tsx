import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { partiesApi } from '../../api/endpoints/parties';
import { usePagedList } from '../../hooks/usePagedList';
import Pagination from '../../components/common/Pagination';
import { toast, extractApiError } from '../../lib/toast';
import ErrorBanner from '../../components/common/ErrorBanner';

type PartyType = { typeCode?: string; code?: string; isActive?: boolean };
type Party = { id: string; code?: string; displayName?: string; city?: string; isActive?: boolean; typeAssignments?: PartyType[]; types?: PartyType[] };

const TYPE_OPTIONS = ['CUSTOMER', 'VENDOR', 'SALES_REP', 'CARRIER'];

const TYPE_BADGE: Record<string, { bg: string; color: string }> = {
  CUSTOMER:  { bg: '#dbeafe', color: '#1d4ed8' },
  VENDOR:    { bg: '#f3e8ff', color: '#6b21a8' },
  SALES_REP: { bg: '#dcfce7', color: '#15803d' },
  CARRIER:   { bg: '#fef9c3', color: '#854d0e' },
};

export default function Parties(): JSX.Element {
  const navigate = useNavigate();
  const list = usePagedList<Party>({
    errorMessage: 'تعذر تحميل الأطراف',
    fetcher: ({ page, pageSize, search }) => partiesApi.getParties({ page, pageSize, searchTerm: search || undefined }),
  });
  const { items: rows, error: listError, loading } = list;
  const [formError, setError] = useState('');
  const error = formError || listError;
  const [showForm, setShowForm] = useState(false);
  const [busy, setBusy] = useState(false);
  const [displayName, setDisplayName] = useState('');
  const [displayNameAr, setDisplayNameAr] = useState('');
  const [taxNumber, setTaxNumber] = useState('');
  const [notes, setNotes] = useState('');
  const [selectedTypes, setSelectedTypes] = useState<string[]>(['CUSTOMER']);

  const load = async (): Promise<void> => { list.reload(); };

  function toggleType(t: string): void {
    setSelectedTypes((prev) => (prev.includes(t) ? prev.filter((x) => x !== t) : [...prev, t]));
  }

  async function create(): Promise<void> {
    if (!displayName.trim() || !displayNameAr.trim()) { setError('الاسم بالعربية والإنجليزية مطلوبان'); return; }
    setBusy(true); setError('');
    try {
      await partiesApi.createParty({ displayName: displayName.trim(), displayNameAr: displayNameAr.trim(), taxNumber: taxNumber.trim() || undefined, notes: notes.trim() || undefined, initialTypeCodes: selectedTypes.length > 0 ? selectedTypes : undefined });
      setDisplayName(''); setDisplayNameAr(''); setTaxNumber(''); setNotes(''); setSelectedTypes(['CUSTOMER']); setShowForm(false);
      toast.success('تم إنشاء الطرف بنجاح'); await load();
    } catch (e: unknown) { toast.error(extractApiError(e, 'تعذر إنشاء الطرف')); setError(extractApiError(e, 'تعذر إنشاء الطرف')); }
    finally { setBusy(false); }
  }

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">الأطراف</h1>
          <div className="vex-page-header__breadcrumb">إدارة العملاء والموردين ومندوبي المبيعات والناقلين</div>
        </div>
        <button type="button" onClick={() => setShowForm((s) => !s)} className={showForm ? 'btn-ghost' : 'btn-primary'}>
          {showForm ? '✕ إلغاء' : '＋ طرف جديد'}
        </button>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      <input
        value={list.searchInput}
        onChange={(e) => list.setSearchInput(e.target.value)}
        placeholder="ابحث بالاسم أو الرقم الضريبي..."
        className="vex-input"
        style={{ marginBottom: 16, maxWidth: 420 }}
      />

      {showForm ? (
        <div className="vex-card" style={{ marginBottom: 20 }}>
          <h2 className="vex-section-title">إضافة طرف جديد</h2>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 16, marginBottom: 20 }}>
            <label className="vex-label">
              الاسم (EN) *
              <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} className="vex-input" />
            </label>
            <label className="vex-label">
              الاسم (AR) *
              <input value={displayNameAr} onChange={(e) => setDisplayNameAr(e.target.value)} className="vex-input" />
            </label>
            <label className="vex-label">
              الرقم الضريبي
              <input value={taxNumber} onChange={(e) => setTaxNumber(e.target.value)} className="vex-input" />
            </label>
            <label className="vex-label">
              ملاحظات
              <input value={notes} onChange={(e) => setNotes(e.target.value)} className="vex-input" />
            </label>
          </div>

          <div style={{ marginBottom: 20 }}>
            <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--txt-muted)', textTransform: 'uppercase', marginBottom: 10 }}>الأنواع الأولية</div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
              {TYPE_OPTIONS.map((t) => {
                const selected = selectedTypes.includes(t);
                const style = TYPE_BADGE[t] ?? { bg: 'var(--clr-surface-2)', color: 'var(--txt-secondary)' };
                return (
                  <button
                    key={t}
                    type="button"
                    onClick={() => toggleType(t)}
                    style={{
                      border: `2px solid ${selected ? style.color : 'var(--clr-border)'}`,
                      borderRadius: 'var(--radius-pill)',
                      padding: '6px 16px',
                      cursor: 'pointer',
                      fontFamily: 'inherit',
                      fontSize: 13,
                      fontWeight: 600,
                      background: selected ? style.bg : '#fff',
                      color: selected ? style.color : 'var(--txt-muted)',
                      transition: 'all var(--transition-fast)',
                    }}
                  >
                    {selected ? '✓ ' : ''}{t}
                  </button>
                );
              })}
            </div>
          </div>

          <button type="button" disabled={busy} onClick={() => void create()} className="btn-primary">
            💾 حفظ الطرف
          </button>
        </div>
      ) : null}

      <div className="vex-card vex-card--no-pad" style={{ opacity: loading ? 0.6 : 1, transition: 'opacity 120ms' }}>
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
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
                    <td>
                      <span style={{ background: 'var(--clr-primary-light)', color: 'var(--clr-primary-dark)', padding: '2px 8px', borderRadius: 'var(--radius-sm)', fontSize: 12, fontWeight: 700 }}>
                        {row.code ?? '-'}
                      </span>
                    </td>
                    <td style={{ fontWeight: 600, color: 'var(--txt-primary)' }}>{row.displayName ?? '-'}</td>
                    <td>
                      <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
                        {typeList.map((type) => {
                          const s = TYPE_BADGE[type] ?? { bg: 'var(--clr-surface-2)', color: 'var(--txt-muted)' };
                          return (
                            <span key={`${row.id}-${type}`} style={{ background: s.bg, color: s.color, borderRadius: 'var(--radius-pill)', padding: '2px 8px', fontSize: 11, fontWeight: 600 }}>
                              {type}
                            </span>
                          );
                        })}
                        {dual ? <span className="badge badge--warning" style={{ fontSize: 11 }}>عميل+مورد</span> : null}
                      </div>
                    </td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{row.city ?? '-'}</td>
                    <td>
                      <span className={`badge ${row.isActive !== false ? 'badge--success' : 'badge--danger'}`}>
                        {row.isActive !== false ? 'نشط' : 'غير نشط'}
                      </span>
                    </td>
                    <td>
                      <button type="button" onClick={() => navigate(`/parties/${row.id}/statement`)} className="btn-secondary" style={{ padding: '5px 14px', fontSize: 12 }}>
                        📄 كشف مدمج
                      </button>
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

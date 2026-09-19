import { useEffect, useMemo, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { partiesApi } from '../../api/endpoints/parties';
import { unwrapList, unwrapNode } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

type PartyNode = { id: string; code?: string; displayName?: string; city?: string };
type StatementRow = { date?: string; entryDate?: string; type?: string; reference?: string; side?: string; debitSyp?: number; creditSyp?: number; balanceSyp?: number };

function extractError(e: unknown, fallback: string): string {
  const r = e as { response?: { data?: { detail?: string; message?: string } } };
  return r.response?.data?.detail ?? r.response?.data?.message ?? fallback;
}

export default function CombinedStatement(): JSX.Element {
  const { id } = useParams();
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [party, setParty] = useState<PartyNode | null>(null);
  const [rows, setRows] = useState<StatementRow[]>([]);

  useEffect(() => {
    if (!id) return;
    let mounted = true;
    async function load(): Promise<void> {
      if (!id) return;
      setLoading(true); setError('');
      try {
        const [partyRes, stRes] = await Promise.all([partiesApi.getPartyById(id), partiesApi.getCombinedStatement(id)]);
        if (!mounted) return;
        setParty(unwrapNode<PartyNode>(partyRes.data));
        setRows(unwrapList<StatementRow>(stRes.data));
      } catch (e: unknown) { if (mounted) setError(extractError(e, 'تعذر تحميل كشف الحساب المدمج')); }
      finally { if (mounted) setLoading(false); }
    }
    void load();
    return () => { mounted = false; };
  }, [id]);

  const balance = useMemo(() => (rows.length > 0 ? Number(rows[rows.length - 1].balanceSyp ?? 0) : 0), [rows]);

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">{party?.displayName ?? 'كشف الحساب المدمج'}</h1>
          <div className="vex-page-header__breadcrumb">
            <span
              onClick={() => navigate('/parties')}
              style={{ color: 'var(--clr-primary)', cursor: 'pointer', textDecoration: 'none' }}
            >الأطراف</span>
            {' / '} كشف الحساب المدمج (عميل + مورد)
          </div>
        </div>
        <button type="button" onClick={() => navigate('/parties')} className="btn-ghost">
          ← رجوع
        </button>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      {/* Summary Row */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 16, marginBottom: 20 }}>
        <div className="vex-card" style={{ gridColumn: 'span 2' }}>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: 16 }}>
            {[
              { label: 'الكود', value: party?.code ?? '-' },
              { label: 'الاسم', value: party?.displayName ?? '-' },
              { label: 'المدينة', value: party?.city ?? '-' },
              { label: 'عدد الحركات', value: rows.length.toString() },
            ].map((item) => (
              <div key={item.label}>
                <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--txt-muted)', textTransform: 'uppercase', marginBottom: 4 }}>{item.label}</div>
                <div style={{ fontSize: 14, fontWeight: 600, color: 'var(--txt-primary)' }}>{item.value}</div>
              </div>
            ))}
          </div>
        </div>

        <div className="vex-card" style={{
          background: balance >= 0 ? 'linear-gradient(135deg,#f0fdf4,#fff)' : 'linear-gradient(135deg,#fef2f2,#fff)',
          borderRight: `4px solid ${balance >= 0 ? '#22c55e' : 'var(--clr-danger)'}`,
        }}>
          <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--txt-muted)', textTransform: 'uppercase', marginBottom: 8 }}>الرصيد الصافي</div>
          <div style={{ fontSize: 26, fontWeight: 800, color: balance >= 0 ? '#22c55e' : 'var(--clr-danger)' }}>
            {balance.toLocaleString('en-US')}
          </div>
          <div style={{ fontSize: 12, color: 'var(--txt-muted)', marginTop: 4 }}>ليرة سورية</div>
        </div>
      </div>

      {/* Statement Table */}
      <div className="vex-card vex-card--no-pad">
        <div style={{ padding: '16px 20px 12px', borderBottom: '1px solid var(--clr-border)' }}>
          <h2 className="vex-section-title" style={{ margin: 0 }}>سجل الحركات</h2>
        </div>
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead>
              <tr>
                <th>التاريخ</th>
                <th>الجهة</th>
                <th>النوع</th>
                <th>المرجع</th>
                <th>مدين</th>
                <th>دائن</th>
                <th>الرصيد</th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 ? (
                <tr><td colSpan={7} style={{ textAlign: 'center', color: 'var(--txt-muted)', padding: '32px 0' }}>لا توجد حركات</td></tr>
              ) : rows.map((row, idx) => {
                const debit = Number(row.debitSyp ?? 0);
                const credit = Number(row.creditSyp ?? 0);
                return (
                  <tr key={`${row.reference ?? 'row'}-${idx}`}>
                    <td style={{ color: 'var(--txt-secondary)' }}>{row.date ?? row.entryDate ?? '-'}</td>
                    <td>
                      {row.side ? <span className="badge badge--draft" style={{ fontSize: 11 }}>{row.side}</span> : '-'}
                    </td>
                    <td>
                      <span style={{ fontSize: 12, color: 'var(--txt-muted)', background: 'var(--clr-surface-2)', padding: '2px 8px', borderRadius: 'var(--radius-sm)' }}>
                        {row.type ?? '-'}
                      </span>
                    </td>
                    <td style={{ fontWeight: 600, color: 'var(--clr-primary)' }}>{row.reference ?? '-'}</td>
                    <td style={{ fontWeight: 600, color: debit > 0 ? 'var(--clr-danger)' : 'var(--txt-muted)' }}>
                      {debit > 0 ? debit.toLocaleString('en-US') : '—'}
                    </td>
                    <td style={{ fontWeight: 600, color: credit > 0 ? '#22c55e' : 'var(--txt-muted)' }}>
                      {credit > 0 ? credit.toLocaleString('en-US') : '—'}
                    </td>
                    <td style={{ fontWeight: 700, color: Number(row.balanceSyp ?? 0) >= 0 ? 'var(--txt-primary)' : 'var(--clr-danger)' }}>
                      {Number(row.balanceSyp ?? 0).toLocaleString('en-US')}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

import { useEffect, useMemo, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { partiesApi } from '../../api/endpoints/parties';
import { unwrapList, unwrapNode } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

type PartyNode = { id: string; code?: string; displayName?: string; city?: string };

type StatementRow = {
  date?: string;
  entryDate?: string;
  type?: string;
  reference?: string;
  side?: string;
  debitSyp?: number;
  creditSyp?: number;
  balanceSyp?: number;
};

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
      setLoading(true);
      setError('');
      try {
        const [partyRes, stRes] = await Promise.all([
          partiesApi.getPartyById(id),
          partiesApi.getCombinedStatement(id),
        ]);
        if (!mounted) return;
        setParty(unwrapNode<PartyNode>(partyRes.data));
        setRows(unwrapList<StatementRow>(stRes.data));
      } catch (e: unknown) {
        if (mounted) setError(extractError(e, 'تعذر تحميل كشف الحساب المدمج'));
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void load();
    return () => { mounted = false; };
  }, [id]);

  const balance = useMemo(
    () => (rows.length > 0 ? Number(rows[rows.length - 1].balanceSyp ?? 0) : 0),
    [rows],
  );

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <h2 style={{ marginTop: 0 }}>كشف الحساب المدمج (عميل + مورد)</h2>
        <button type="button" onClick={() => navigate('/parties')} style={{ border: 'none', borderRadius: '8px', background: '#607d8b', color: '#fff', padding: '8px 12px', cursor: 'pointer' }}>رجوع</button>
      </div>
      {error ? <ErrorBanner message={error} /> : null}

      <div style={{ display: 'flex', gap: '12px', marginBottom: '12px' }}>
        <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', padding: '12px', flex: 1 }}>
          <h3 style={{ margin: 0 }}>{party?.displayName ?? '-'}</h3>
          <div style={{ color: '#607d8b' }}>{party?.code} | {party?.city ?? '-'}</div>
        </div>
        <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', padding: '12px', minWidth: '220px' }}>
          <div style={{ color: '#607d8b' }}>الرصيد الصافي</div>
          <div style={{ color: balance >= 0 ? '#00796b' : '#c62828', fontSize: '24px', fontWeight: 800 }}>{balance.toLocaleString('en-US')} ل.س</div>
        </div>
      </div>

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
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
              <tr><td colSpan={7} style={{ textAlign: 'center', padding: '16px', color: '#777' }}>لا توجد حركات</td></tr>
            ) : rows.map((row, idx) => (
              <tr key={`${row.reference ?? 'row'}-${idx}`}>
                <td>{row.date ?? row.entryDate ?? '-'}</td>
                <td>{row.side ?? '-'}</td>
                <td>{row.type ?? '-'}</td>
                <td>{row.reference ?? '-'}</td>
                <td>{Number(row.debitSyp ?? 0).toLocaleString('en-US')}</td>
                <td>{Number(row.creditSyp ?? 0).toLocaleString('en-US')}</td>
                <td>{Number(row.balanceSyp ?? 0).toLocaleString('en-US')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

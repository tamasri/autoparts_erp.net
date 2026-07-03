import { useEffect, useState } from 'react';
import { partiesApi } from '../../api/endpoints/parties';
import { unwrapList } from '../../api/apiData';
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


export default function Parties(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<Party[]>([]);

  useEffect(() => {
    let mounted = true;
    async function load(): Promise<void> {
      setLoading(true);
      setError('');
      try {
        const res = await partiesApi.getParties({ page: 1, pageSize: 50 });
        if (mounted) setRows(unwrapList<Party>(res.data));
      } catch (e: unknown) {
        if (!mounted) return;
        const msg = (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.detail
          ?? (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.message
          ?? 'تعذر تحميل الأطراف';
        setError(msg);
      } finally {
        if (mounted) setLoading(false);
      }
    }
    void load();
    return () => {
      mounted = false;
    };
  }, []);

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      {error ? <ErrorBanner message={error} /> : null}
      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th>الكود</th>
              <th>الاسم</th>
              <th>الأنواع</th>
              <th>المدينة</th>
              <th>الحالة</th>
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
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}

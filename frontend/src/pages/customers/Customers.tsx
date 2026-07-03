import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { customersApi } from '../../api/endpoints/customers';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';
import StatusBadge from '../../components/common/StatusBadge';

type Customer = {
  id: string;
  code: string;
  name: string;
  type: string;
  city?: string;
  creditLimitSyp?: number;
  balanceSyp?: number;
  isActive?: boolean;
};


function typeBadgeColor(type: string): string {
  const normalized = type.toUpperCase();
  if (normalized === 'WORKSHOP') return '#1976d2';
  if (normalized === 'RETAIL') return '#ef6c00';
  if (normalized === 'WHOLESALE') return '#7b1fa2';
  return '#607d8b';
}

export default function Customers(): JSX.Element {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [rows, setRows] = useState<Customer[]>([]);

  useEffect(() => {
    let mounted = true;
    async function load(): Promise<void> {
      setLoading(true);
      setError('');
      try {
        const res = await customersApi.getCustomers({ page: 1, pageSize: 50 });
        if (mounted) setRows(unwrapList<Customer>(res.data));
      } catch (e: unknown) {
        if (!mounted) return;
        const msg = (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.detail
          ?? (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.message
          ?? 'تعذر تحميل العملاء';
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

  const filtered = useMemo(
    () => rows.filter((r) => r.name?.toLowerCase().includes(search.toLowerCase())),
    [rows, search],
  );

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
        <button
          type="button"
          onClick={() => window.alert('سيتم إضافة نموذج إنشاء العميل في تحديث لاحق')}
          style={{ border: 'none', borderRadius: '8px', background: '#00796b', color: '#fff', padding: '8px 12px' }}
        >
          عميل جديد
        </button>
      </div>

      <div style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', overflow: 'auto' }}>
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
                <td><button type="button" style={{ border: 'none', background: '#eceff1', borderRadius: '6px', padding: '4px 8px' }}>عرض</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

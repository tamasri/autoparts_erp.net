import { useEffect, useState } from 'react';
import { rolesApi } from '../../api/endpoints/roles';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

type Role = {
  id: string;
  code?: string;
  name?: string;
  description?: string;
  permissions?: string[];
};


export default function Roles(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<Role[]>([]);

  useEffect(() => {
    let mounted = true;
    async function load(): Promise<void> {
      setLoading(true);
      setError('');
      try {
        const res = await rolesApi.getRoles();
        if (mounted) setRows(unwrapList<Role>(res.data));
      } catch (e: unknown) {
        if (!mounted) return;
        const msg = (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.detail
          ?? (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.message
          ?? 'تعذر تحميل الأدوار';
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
    <div style={{ direction: 'rtl', display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(240px,1fr))', gap: '12px' }}>
      {error ? <ErrorBanner message={error} /> : null}
      {rows.map((role) => (
        <div key={role.id} style={{ background: '#fff', borderRadius: '12px', boxShadow: '0 2px 8px #00000012', padding: '12px' }}>
          <h3 style={{ marginTop: 0 }}>{role.code ?? role.name ?? '-'}</h3>
          <p style={{ color: '#607d8b' }}>{role.description ?? 'لا يوجد وصف'}</p>
          <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap' }}>
            {(role.permissions ?? []).map((p) => (
              <span key={`${role.id}-${p}`} style={{ background: '#e0f2f1', color: '#00695c', borderRadius: '999px', padding: '2px 8px', fontSize: '12px' }}>
                {p}
              </span>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}

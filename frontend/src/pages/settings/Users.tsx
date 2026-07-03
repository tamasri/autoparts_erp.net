import { useEffect, useState } from 'react';
import { usersApi } from '../../api/endpoints/users';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

type User = {
  id: string;
  userName?: string;
  username?: string;
  fullName?: string;
  roleCodes?: string[];
  roles?: string[];
  isActive?: boolean;
  lastLoginAt?: string;
};

export default function Users(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<User[]>([]);

  useEffect(() => {
    let mounted = true;
    async function load(): Promise<void> {
      setLoading(true);
      setError('');
      try {
        const res = await usersApi.getUsers(1, 50);
        if (mounted) setRows(unwrapList<User>(res.data));
      } catch (e: unknown) {
        if (!mounted) return;
        const msg = (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.detail
          ?? (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.message
          ?? 'تعذر تحميل المستخدمين';
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
              <th>اسم المستخدم</th>
              <th>الاسم الكامل</th>
              <th>الأدوار</th>
              <th>الحالة</th>
              <th>آخر دخول</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id}>
                <td>{row.userName ?? row.username ?? '-'}</td>
                <td>{row.fullName ?? '-'}</td>
                <td>{(row.roleCodes ?? row.roles ?? []).join(', ') || '-'}</td>
                <td>{row.isActive ?? true ? 'نشط' : 'غير نشط'}</td>
                <td>{row.lastLoginAt ?? '-'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

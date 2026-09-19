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

const ROLE_COLORS: Record<string, { bg: string; color: string }> = {
  ADMIN:        { bg: '#f3e8ff', color: '#6b21a8' },
  SALES_MANAGER: { bg: '#dbeafe', color: '#1d4ed8' },
  CASHIER:      { bg: '#dcfce7', color: '#15803d' },
  ACCOUNTANT:   { bg: '#fef9c3', color: '#854d0e' },
  WAREHOUSE:    { bg: '#fef3c7', color: '#92400e' },
};

export default function Users(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<User[]>([]);

  useEffect(() => {
    let mounted = true;
    async function load(): Promise<void> {
      setLoading(true); setError('');
      try {
        const res = await usersApi.getUsers(1, 50);
        if (mounted) setRows(unwrapList<User>(res.data));
      } catch (e: unknown) {
        if (!mounted) return;
        const msg = (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.detail
          ?? (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.message
          ?? 'تعذر تحميل المستخدمين';
        setError(msg);
      } finally { if (mounted) setLoading(false); }
    }
    void load();
    return () => { mounted = false; };
  }, []);

  if (loading) return <LoadingSpinner />;

  const activeCount = rows.filter((r) => r.isActive ?? true).length;

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">إدارة المستخدمين</h1>
          <div className="vex-page-header__breadcrumb">عرض وإدارة حسابات المستخدمين وصلاحياتهم</div>
        </div>
        <div style={{ display: 'flex', gap: 10 }}>
          <div style={{ background: '#f0fdf4', color: '#15803d', border: '1px solid #bbf7d0', borderRadius: 'var(--radius-pill)', padding: '6px 14px', fontSize: 13, fontWeight: 700 }}>
            ✓ نشط: {activeCount}
          </div>
          <div style={{ background: 'var(--clr-surface-2)', color: 'var(--txt-muted)', border: '1px solid var(--clr-border)', borderRadius: 'var(--radius-pill)', padding: '6px 14px', fontSize: 13, fontWeight: 700 }}>
            إجمالي: {rows.length}
          </div>
        </div>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      <div className="vex-card vex-card--no-pad">
        <div style={{ overflowX: 'auto' }}>
          <table className="vex-table">
            <thead>
              <tr>
                <th>المستخدم</th>
                <th>الاسم الكامل</th>
                <th>الأدوار</th>
                <th>الحالة</th>
                <th>آخر دخول</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => {
                const username = row.userName ?? row.username ?? '-';
                const roles = row.roleCodes ?? row.roles ?? [];
                const isActive = row.isActive ?? true;
                return (
                  <tr key={row.id}>
                    <td>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                        <div style={{
                          width: 36, height: 36, borderRadius: '50%',
                          background: 'linear-gradient(135deg, var(--clr-primary), var(--clr-primary-mid))',
                          color: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center',
                          fontSize: 13, fontWeight: 700, flexShrink: 0,
                        }}>
                          {username[0]?.toUpperCase() ?? '?'}
                        </div>
                        <div>
                          <div style={{ fontWeight: 600, color: 'var(--txt-primary)' }}>{username}</div>
                          <div style={{ fontSize: 11, color: 'var(--txt-muted)' }}>{row.id.slice(0, 8)}</div>
                        </div>
                      </div>
                    </td>
                    <td style={{ color: 'var(--txt-secondary)' }}>{row.fullName ?? '-'}</td>
                    <td>
                      <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
                        {roles.length > 0 ? roles.map((r) => {
                          const s = ROLE_COLORS[r] ?? { bg: 'var(--clr-surface-2)', color: 'var(--txt-muted)' };
                          return (
                            <span key={`${row.id}-${r}`} style={{ background: s.bg, color: s.color, borderRadius: 'var(--radius-pill)', padding: '2px 8px', fontSize: 11, fontWeight: 600 }}>
                              {r}
                            </span>
                          );
                        }) : <span style={{ color: 'var(--txt-muted)', fontSize: 12 }}>—</span>}
                      </div>
                    </td>
                    <td>
                      <span className={`badge ${isActive ? 'badge--success' : 'badge--danger'}`}>
                        {isActive ? 'نشط' : 'غير نشط'}
                      </span>
                    </td>
                    <td style={{ color: 'var(--txt-secondary)', fontSize: 12 }}>
                      {row.lastLoginAt ? new Date(row.lastLoginAt).toLocaleString('ar') : '—'}
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

import { useEffect, useState } from 'react';
import { rolesApi } from '../../api/endpoints/roles';
import { unwrapList } from '../../api/apiData';
import ErrorBanner from '../../components/common/ErrorBanner';
import LoadingSpinner from '../../components/common/LoadingSpinner';

type Role = { id: string; code?: string; name?: string; description?: string; permissions?: string[] };

const PERM_MODULE_COLORS: Record<string, { bg: string; color: string }> = {
  INV:   { bg: '#dbeafe', color: '#1d4ed8' },
  SALES: { bg: '#dcfce7', color: '#15803d' },
  FIN:   { bg: '#f3e8ff', color: '#6b21a8' },
  HR:    { bg: '#fef9c3', color: '#854d0e' },
  SYS:   { bg: '#fef2f2', color: '#b91c1c' },
};

function permColor(p: string): { bg: string; color: string } {
  for (const [prefix, style] of Object.entries(PERM_MODULE_COLORS)) {
    if (p.startsWith(prefix)) return style;
  }
  return { bg: 'var(--clr-surface-2)', color: 'var(--txt-muted)' };
}

export default function Roles(): JSX.Element {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [rows, setRows] = useState<Role[]>([]);

  useEffect(() => {
    let mounted = true;
    async function load(): Promise<void> {
      setLoading(true); setError('');
      try {
        const res = await rolesApi.getRoles();
        if (mounted) setRows(unwrapList<Role>(res.data));
      } catch (e: unknown) {
        if (!mounted) return;
        const msg = (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.detail
          ?? (e as { response?: { data?: { detail?: string; message?: string } } }).response?.data?.message
          ?? 'تعذر تحميل الأدوار';
        setError(msg);
      } finally { if (mounted) setLoading(false); }
    }
    void load();
    return () => { mounted = false; };
  }, []);

  if (loading) return <LoadingSpinner />;

  return (
    <div style={{ direction: 'rtl' }}>
      <div className="vex-page-header">
        <div>
          <h1 className="vex-page-header__title">الأدوار والصلاحيات</h1>
          <div className="vex-page-header__breadcrumb">إدارة أدوار المستخدمين وصلاحيات الوصول</div>
        </div>
        <div style={{ background: 'var(--clr-surface-2)', color: 'var(--txt-muted)', border: '1px solid var(--clr-border)', borderRadius: 'var(--radius-pill)', padding: '6px 14px', fontSize: 13, fontWeight: 700 }}>
          {rows.length} دور
        </div>
      </div>

      {error ? <ErrorBanner message={error} /> : null}

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))', gap: 16 }}>
        {rows.map((role) => {
          const perms = role.permissions ?? [];
          return (
            <div key={role.id} className="vex-card" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              {/* Role Header */}
              <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                <div style={{
                  width: 40, height: 40, borderRadius: 'var(--radius-md)',
                  background: 'linear-gradient(135deg, var(--clr-primary), var(--clr-primary-mid))',
                  color: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center',
                  fontSize: 16, fontWeight: 700, flexShrink: 0,
                }}>
                  🛡
                </div>
                <div>
                  <h3 style={{ margin: 0, fontWeight: 700, fontSize: 15, color: 'var(--txt-primary)' }}>
                    {role.code ?? role.name ?? '-'}
                  </h3>
                  <p style={{ margin: 0, fontSize: 12, color: 'var(--txt-muted)', marginTop: 2 }}>
                    {role.description ?? 'لا يوجد وصف'}
                  </p>
                </div>
              </div>

              {/* Divider */}
              <div style={{ borderTop: '1px solid var(--clr-border)' }} />

              {/* Permissions */}
              <div>
                <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--txt-muted)', textTransform: 'uppercase', marginBottom: 8 }}>
                  الصلاحيات ({perms.length})
                </div>
                {perms.length > 0 ? (
                  <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                    {perms.map((p) => {
                      const s = permColor(p);
                      return (
                        <span key={`${role.id}-${p}`} style={{
                          background: s.bg, color: s.color,
                          borderRadius: 'var(--radius-sm)', padding: '3px 8px',
                          fontSize: 11, fontWeight: 600, fontFamily: 'monospace',
                        }}>
                          {p}
                        </span>
                      );
                    })}
                  </div>
                ) : (
                  <span style={{ color: 'var(--txt-muted)', fontSize: 12 }}>لا توجد صلاحيات محددة</span>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuthStore } from '../../stores/authStore';

type NavItem = {
  to: string;
  label: string;
  icon: string;
};

const navItems: NavItem[] = [
  { to: '/', label: 'لوحة التحكم', icon: '📊' },
  { to: '/customers', label: 'العملاء والأطراف', icon: '👥' },
  { to: '/invoices', label: 'الفواتير', icon: '🧾' },
  { to: '/inventory', label: 'المخزون', icon: '📦' },
  { to: '/inventory/receiving', label: 'الاستلام والتخزين', icon: '📥' },
  { to: '/inventory/transfers', label: 'التحويلات', icon: '🔁' },
  { to: '/inventory/issue-orders', label: 'أوامر الصرف', icon: '📤' },
  { to: '/inventory/cycle-counts', label: 'الجرد الدوري', icon: '🧮' },
  { to: '/inventory/adjustments', label: 'تسويات المخزون', icon: '⚖️' },
  { to: '/inventory/alerts', label: 'تنبيهات المخزون', icon: '🚨' },
  { to: '/parties', label: 'الأطراف', icon: '🤝' },
  { to: '/approvals', label: 'الموافقات', icon: '✅' },
  { to: '/audit', label: 'سجل التدقيق', icon: '🔍' },
  { to: '/periods', label: 'إقفال الفترات', icon: '🔒' },
  { to: '/users', label: 'المستخدمون والأدوار', icon: '⚙️' },
];

export default function AppLayout(): JSX.Element {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useAuthStore((s) => s.logout);

  return (
    <div style={{ minHeight: '100vh', display: 'flex', direction: 'rtl', background: '#f1f5f4' }}>
      <aside
        style={{
          width: '220px',
          background: '#00695c',
          color: '#fff',
          padding: '12px',
          display: 'flex',
          flexDirection: 'column',
          gap: '8px',
        }}
      >
        {navItems.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.to === '/'}
            style={({ isActive }) => ({
              color: '#fff',
              textDecoration: 'none',
              padding: '10px 12px',
              borderRadius: '8px',
              background: isActive ? '#004d40' : 'transparent',
              fontSize: '14px',
              display: 'block',
            })}
          >
            <span style={{ marginLeft: '8px' }}>{item.icon}</span>
            {item.label}
          </NavLink>
        ))}
      </aside>

      <main style={{ flex: 1, minWidth: 0 }}>
        <header
          style={{
            height: '56px',
            background: '#00796b',
            color: '#fff',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '0 16px',
          }}
        >
          <div style={{ fontWeight: 700 }}>AutoParts ERP</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
            <span>{user?.fullName ?? user?.username ?? 'مستخدم'}</span>
            <button
              type="button"
              onClick={() => {
                logout();
                navigate('/login');
              }}
              style={{
                border: 'none',
                borderRadius: '8px',
                background: '#004d40',
                color: '#fff',
                padding: '8px 12px',
                cursor: 'pointer',
              }}
            >
              تسجيل خروج
            </button>
          </div>
        </header>
        <div style={{ padding: '16px' }}>
          <Outlet />
        </div>
      </main>
    </div>
  );
}

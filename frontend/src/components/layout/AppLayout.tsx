import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuthStore } from '../../stores/authStore';

type NavItem = { to: string; label: string; icon: string };
type NavGroup = { title: string; icon: string; items: NavItem[] };

const navGroups: NavGroup[] = [
  {
    title: 'الإدارة العليا',
    icon: '📊',
    items: [
      { to: '/', label: 'لوحة التحكم', icon: '🏠' },
      { to: '/kpi', label: 'مؤشرات الأداء', icon: '📈' },
    ],
  },
  {
    title: 'المبيعات والمحاسبة',
    icon: '💰',
    items: [
      { to: '/customers', label: 'العملاء', icon: '👥' },
      { to: '/invoices', label: 'الفواتير', icon: '🧾' },
      { to: '/fx-rates', label: 'أسعار الصرف', icon: '💱' },
      { to: '/parties', label: 'الأطراف', icon: '🤝' },
    ],
  },
  {
    title: 'المشتريات والمخازن',
    icon: '📦',
    items: [
      { to: '/inventory', label: 'المخزون', icon: '📦' },
      { to: '/inventory/receiving', label: 'الاستلام', icon: '📥' },
      { to: '/inventory/transfers', label: 'التحويلات', icon: '🔁' },
      { to: '/inventory/issue-orders', label: 'أوامر الصرف', icon: '📤' },
      { to: '/inventory/cycle-counts', label: 'الجرد الدوري', icon: '🧮' },
      { to: '/inventory/adjustments', label: 'التسويات', icon: '⚖️' },
      { to: '/inventory/alerts', label: 'التنبيهات', icon: '🚨' },
    ],
  },
  {
    title: 'الإدارة والرقابة',
    icon: '🛡️',
    items: [
      { to: '/approvals', label: 'الموافقات', icon: '✅' },
      { to: '/audit', label: 'سجل التدقيق', icon: '🔍' },
      { to: '/periods', label: 'إقفال الفترات', icon: '🔒' },
      { to: '/users', label: 'المستخدمون', icon: '👤' },
      { to: '/roles', label: 'الأدوار', icon: '⚙️' },
    ],
  },
];

export default function AppLayout(): JSX.Element {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useAuthStore((s) => s.logout);

  return (
    <div style={{ minHeight: '100vh', display: 'flex', direction: 'rtl', background: '#f1f5f4' }}>
      <aside style={{ width: '220px', background: '#00695c', color: '#fff', padding: '8px 0', display: 'flex', flexDirection: 'column', overflowY: 'auto' }}>
        <div style={{ padding: '12px 14px 8px', fontWeight: 800, fontSize: '15px', letterSpacing: '0.5px', borderBottom: '1px solid #00796b', marginBottom: '4px' }}>
          AutoParts ERP
        </div>
        {navGroups.map((group) => (
          <div key={group.title}>
            <div style={{ padding: '10px 14px 4px', fontSize: '11px', fontWeight: 700, color: '#b2dfdb', textTransform: 'uppercase', letterSpacing: '0.8px' }}>
              {group.icon} {group.title}
            </div>
            {group.items.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.to === '/'}
                style={({ isActive }) => ({
                  color: '#fff',
                  textDecoration: 'none',
                  padding: '8px 14px 8px 10px',
                  borderRadius: '0 8px 8px 0',
                  marginLeft: '6px',
                  background: isActive ? '#004d40' : 'transparent',
                  fontSize: '13px',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '8px',
                  marginBottom: '1px',
                })}
              >
                <span>{item.icon}</span>
                {item.label}
              </NavLink>
            ))}
          </div>
        ))}
      </aside>

      <main style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}>
        <header style={{ height: '56px', background: '#00796b', color: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '0 16px', flexShrink: 0 }}>
          <div style={{ fontWeight: 700 }}>نظام إدارة قطع الغيار</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
            <span style={{ fontSize: '13px' }}>{user?.fullName ?? user?.username ?? 'مستخدم'}</span>
            <button
              type="button"
              onClick={() => { logout(); navigate('/login'); }}
              style={{ border: 'none', borderRadius: '8px', background: '#004d40', color: '#fff', padding: '8px 12px', cursor: 'pointer', fontSize: '13px' }}
            >
              تسجيل خروج
            </button>
          </div>
        </header>
        <div style={{ padding: '16px', flex: 1, overflowY: 'auto' }}>
          <Outlet />
        </div>
      </main>
    </div>
  );
}

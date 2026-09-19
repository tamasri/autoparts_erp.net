import { useState, useRef, useEffect } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuthStore } from '../../stores/authStore';

type NavItem = { to: string; label: string; icon: string };
type NavGroup = { title: string; icon: string; items: NavItem[] };

const navGroups: NavGroup[] = [
  {
    title: 'الإدارة العليا',
    icon: '📊',
    items: [
      { to: '/', label: 'لوحة التحكم', icon: '⊞' },
      { to: '/kpi', label: 'مؤشرات الأداء', icon: '↗' },
    ],
  },
  {
    title: 'المبيعات والمحاسبة',
    icon: '💰',
    items: [
      { to: '/customers', label: 'العملاء', icon: '◎' },
      { to: '/invoices', label: 'الفواتير', icon: '☰' },
      { to: '/fx-rates', label: 'أسعار الصرف', icon: '⇄' },
      { to: '/parties', label: 'الأطراف', icon: '⊂' },
    ],
  },
  {
    title: 'المشتريات والمخازن',
    icon: '📦',
    items: [
      { to: '/items', label: 'الأصناف', icon: '◧' },
      { to: '/inventory', label: 'المخزون', icon: '▦' },
      { to: '/inventory/receiving', label: 'الاستلام', icon: '↙' },
      { to: '/inventory/transfers', label: 'التحويلات', icon: '⇌' },
      { to: '/inventory/issue-orders', label: 'أوامر الصرف', icon: '↗' },
      { to: '/inventory/cycle-counts', label: 'الجرد الدوري', icon: '↻' },
      { to: '/inventory/adjustments', label: 'التسويات', icon: '⇔' },
      { to: '/inventory/alerts', label: 'التنبيهات', icon: '◬' },
    ],
  },
  {
    title: 'الإدارة والرقابة',
    icon: '🛡️',
    items: [
      { to: '/approvals', label: 'الموافقات', icon: '✓' },
      { to: '/audit', label: 'سجل التدقيق', icon: '◎' },
      { to: '/accounting/sync', label: 'مزامنة المحاسبة', icon: '⇄' },
      { to: '/periods', label: 'إقفال الفترات', icon: '⊝' },
      { to: '/users', label: 'المستخدمون', icon: '◉' },
      { to: '/roles', label: 'الأدوار', icon: '◈' },
    ],
  },
];

// Quick-links shown inside the Mega Menu
const megaMenuSections = [
  {
    title: 'الصفحات',
    links: [
      { label: 'لوحة التحكم', to: '/' },
      { label: 'الفواتير', to: '/invoices' },
      { label: 'العملاء', to: '/customers' },
      { label: 'المخزون', to: '/inventory' },
      { label: 'التقارير', to: '/kpi' },
    ],
  },
  {
    title: 'الإجراءات',
    links: [
      { label: 'فاتورة جديدة', to: '/invoices/new' },
      { label: 'استلام بضاعة', to: '/inventory/receiving' },
      { label: 'تحويل مخزون', to: '/inventory/transfers' },
      { label: 'الموافقات', to: '/approvals' },
      { label: 'التدقيق', to: '/audit' },
    ],
  },
  {
    title: 'الإعدادات',
    links: [
      { label: 'المستخدمون', to: '/users' },
      { label: 'الأدوار', to: '/roles' },
      { label: 'إقفال الفترات', to: '/periods' },
      { label: 'أسعار الصرف', to: '/fx-rates' },
      { label: 'الأطراف', to: '/parties' },
    ],
  },
];

function getInitials(name: string): string {
  const parts = name.trim().split(' ');
  if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
  return name.slice(0, 2).toUpperCase();
}

export default function AppLayout(): JSX.Element {
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const logout = useAuthStore((s) => s.logout);

  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
  const [megaOpen, setMegaOpen] = useState(false);
  const [userMenuOpen, setUserMenuOpen] = useState(false);
  const megaRef = useRef<HTMLDivElement>(null);
  const userRef = useRef<HTMLDivElement>(null);

  // Close dropdowns on outside click
  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (megaRef.current && !megaRef.current.contains(e.target as Node)) {
        setMegaOpen(false);
      }
      if (userRef.current && !userRef.current.contains(e.target as Node)) {
        setUserMenuOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, []);

  const displayName = user?.fullName ?? user?.username ?? 'مستخدم';
  const initials = getInitials(displayName);

  return (
    <div style={{ minHeight: '100vh', display: 'flex', direction: 'rtl', background: 'var(--clr-bg)' }}>

      {/* ────────── SIDEBAR ────────── */}
      <aside
        style={{
          width: sidebarCollapsed ? 'var(--sidebar-collapsed)' : 'var(--sidebar-width)',
          background: 'var(--sidebar-bg)',
          color: 'var(--sidebar-text)',
          display: 'flex',
          flexDirection: 'column',
          flexShrink: 0,
          overflowY: 'auto',
          overflowX: 'hidden',
          transition: 'width var(--transition-base)',
          position: 'sticky',
          top: 0,
          height: '100vh',
          zIndex: 100,
        }}
      >
        {/* Logo */}
        <div style={{
          height: 'var(--topbar-height)',
          display: 'flex',
          alignItems: 'center',
          gap: '10px',
          padding: '0 16px',
          borderBottom: '1px solid rgba(255,255,255,0.06)',
          flexShrink: 0,
        }}>
          <div style={{
            width: 36,
            height: 36,
            borderRadius: 10,
            background: 'linear-gradient(135deg, #5c54ff, #7b75ff)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: 16,
            fontWeight: 800,
            color: '#fff',
            flexShrink: 0,
            boxShadow: '0 4px 12px rgba(92,84,255,0.4)',
          }}>A</div>
          {!sidebarCollapsed && (
            <span style={{ fontSize: 15, fontWeight: 700, color: '#fff', whiteSpace: 'nowrap' }}>
              AutoParts ERP
            </span>
          )}
        </div>

        {/* Nav Groups */}
        <nav style={{ flex: 1, padding: '12px 0', overflowY: 'auto', overflowX: 'hidden' }}>
          {navGroups.map((group) => (
            <div key={group.title} style={{ marginBottom: 4 }}>
              {!sidebarCollapsed && (
                <div style={{
                  padding: '10px 18px 4px',
                  fontSize: 10,
                  fontWeight: 700,
                  color: 'var(--sidebar-label)',
                  textTransform: 'uppercase',
                  letterSpacing: '0.9px',
                  whiteSpace: 'nowrap',
                  overflow: 'hidden',
                }}>
                  {group.title}
                </div>
              )}
              {group.items.map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  end={item.to === '/'}
                  title={sidebarCollapsed ? item.label : undefined}
                  style={({ isActive }) => ({
                    display: 'flex',
                    alignItems: 'center',
                    gap: 10,
                    padding: sidebarCollapsed ? '11px 0' : '10px 18px',
                    justifyContent: sidebarCollapsed ? 'center' : 'flex-start',
                    margin: '1px 8px',
                    borderRadius: 'var(--radius-sm)',
                    textDecoration: 'none',
                    fontSize: 13.5,
                    fontWeight: isActive ? 600 : 400,
                    color: isActive ? 'var(--sidebar-text-active)' : 'var(--sidebar-text)',
                    background: isActive ? 'var(--sidebar-active)' : 'transparent',
                    borderRight: isActive ? '3px solid var(--sidebar-active-bar)' : '3px solid transparent',
                    transition: 'all var(--transition-fast)',
                    whiteSpace: 'nowrap',
                    overflow: 'hidden',
                    position: 'relative',
                  })}
                  onMouseEnter={(e) => {
                    (e.currentTarget as HTMLElement).style.background = 'rgba(255,255,255,0.06)';
                  }}
                  onMouseLeave={(e) => {
                    // Restore only if not active
                    const link = e.currentTarget as HTMLElement;
                    if (!link.classList.contains('active')) {
                      link.style.background = '';
                    }
                  }}
                >
                  <span style={{ fontSize: 15, width: 20, textAlign: 'center', flexShrink: 0 }}>
                    {item.icon}
                  </span>
                  {!sidebarCollapsed && item.label}
                </NavLink>
              ))}
            </div>
          ))}
        </nav>

        {/* Sidebar Toggle */}
        <button
          type="button"
          onClick={() => setSidebarCollapsed((v) => !v)}
          style={{
            margin: '12px 10px',
            padding: '10px',
            background: 'rgba(255,255,255,0.05)',
            border: 'none',
            borderRadius: 'var(--radius-sm)',
            color: 'var(--sidebar-text)',
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: 16,
            transition: 'background var(--transition-fast)',
            flexShrink: 0,
          }}
          title={sidebarCollapsed ? 'توسيع القائمة' : 'طي القائمة'}
        >
          {sidebarCollapsed ? '→' : '←'}
        </button>
      </aside>

      {/* ────────── MAIN AREA ────────── */}
      <main style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>

        {/* ────────── TOP BAR ────────── */}
        <header style={{
          height: 'var(--topbar-height)',
          background: 'var(--topbar-bg)',
          boxShadow: 'var(--topbar-shadow)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '0 20px',
          flexShrink: 0,
          position: 'sticky',
          top: 0,
          zIndex: 90,
          gap: 12,
        }}>

          {/* Left side: search + mega menu */}
          <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>

            {/* Search */}
            <div style={{ position: 'relative' }}>
              <span style={{
                position: 'absolute',
                right: 10,
                top: '50%',
                transform: 'translateY(-50%)',
                color: 'var(--txt-muted)',
                fontSize: 14,
                pointerEvents: 'none',
              }}>🔍</span>
              <input
                type="search"
                placeholder="بحث..."
                style={{
                  padding: '8px 34px 8px 14px',
                  border: '1.5px solid var(--clr-border)',
                  borderRadius: 'var(--radius-pill)',
                  fontSize: 13,
                  color: 'var(--txt-primary)',
                  background: 'var(--clr-surface-2)',
                  outline: 'none',
                  width: 200,
                  transition: 'border-color var(--transition-fast), width var(--transition-base)',
                  textAlign: 'right',
                }}
                onFocus={(e) => {
                  e.currentTarget.style.borderColor = 'var(--clr-primary)';
                  e.currentTarget.style.width = '260px';
                }}
                onBlur={(e) => {
                  e.currentTarget.style.borderColor = 'var(--clr-border)';
                  e.currentTarget.style.width = '200px';
                }}
              />
            </div>

            {/* Mega Menu */}
            <div ref={megaRef} style={{ position: 'relative' }}>
              <button
                type="button"
                id="mega-menu-trigger"
                onClick={() => setMegaOpen((v) => !v)}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 6,
                  padding: '8px 14px',
                  background: megaOpen ? 'var(--clr-primary-light)' : 'transparent',
                  border: 'none',
                  borderRadius: 'var(--radius-sm)',
                  color: megaOpen ? 'var(--clr-primary)' : 'var(--txt-secondary)',
                  fontSize: 13.5,
                  fontWeight: 600,
                  cursor: 'pointer',
                  transition: 'all var(--transition-fast)',
                }}
              >
                <span>القائمة الرئيسية</span>
                <span style={{ fontSize: 10, transition: 'transform var(--transition-fast)', transform: megaOpen ? 'rotate(180deg)' : 'rotate(0deg)' }}>▼</span>
              </button>

              {/* Mega Menu Panel */}
              {megaOpen && (
                <div
                  id="mega-menu-panel"
                  style={{
                    position: 'absolute',
                    top: 'calc(100% + 12px)',
                    right: 0,
                    width: 680,
                    background: 'var(--clr-surface)',
                    borderRadius: 'var(--radius-lg)',
                    boxShadow: '0 16px 48px rgba(0,0,0,0.12), 0 4px 16px rgba(0,0,0,0.06)',
                    border: '1px solid var(--clr-border)',
                    padding: 24,
                    display: 'grid',
                    gridTemplateColumns: '1fr 1fr 1fr 200px',
                    gap: 20,
                    zIndex: 200,
                    animation: 'fadeIn 0.15s ease',
                  }}
                >
                  <style>{`@keyframes fadeIn { from { opacity:0; transform:translateY(-6px) } to { opacity:1; transform:none } }`}</style>
                  {megaMenuSections.map((sec) => (
                    <div key={sec.title}>
                      <div style={{
                        fontSize: 11,
                        fontWeight: 700,
                        color: 'var(--txt-muted)',
                        textTransform: 'uppercase',
                        letterSpacing: '0.6px',
                        marginBottom: 10,
                      }}>{sec.title}</div>
                      <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                        {sec.links.map((l) => (
                          <NavLink
                            key={l.to}
                            to={l.to}
                            onClick={() => setMegaOpen(false)}
                            style={({ isActive }) => ({
                              display: 'flex',
                              alignItems: 'center',
                              gap: 8,
                              padding: '7px 10px',
                              borderRadius: 'var(--radius-sm)',
                              fontSize: 13,
                              textDecoration: 'none',
                              color: isActive ? 'var(--clr-primary)' : 'var(--txt-secondary)',
                              background: isActive ? 'var(--clr-primary-light)' : 'transparent',
                              transition: 'all var(--transition-fast)',
                              fontWeight: isActive ? 600 : 400,
                            })}
                            onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.background = 'var(--clr-surface-2)'; }}
                            onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.background = ''; }}
                          >
                            <span style={{ width: 6, height: 6, borderRadius: '50%', background: 'var(--clr-primary)', flexShrink: 0, opacity: 0.5 }} />
                            {l.label}
                          </NavLink>
                        ))}
                      </div>
                    </div>
                  ))}

                  {/* Promo Card */}
                  <div className="vex-promo-card">
                    <div style={{
                      width: 40,
                      height: 40,
                      background: 'linear-gradient(135deg, #5c54ff, #7b75ff)',
                      borderRadius: 12,
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      fontSize: 18,
                      color: '#fff',
                    }}>⚡</div>
                    <div className="vex-promo-card__title">نظام إدارة متكامل</div>
                    <div className="vex-promo-card__desc">
                      إدارة المبيعات والمشتريات والمخزون في مكان واحد
                    </div>
                    <NavLink
                      to="/kpi"
                      onClick={() => setMegaOpen(false)}
                      style={{
                        display: 'inline-block',
                        padding: '8px 16px',
                        background: 'var(--clr-primary)',
                        color: '#fff',
                        borderRadius: 'var(--radius-pill)',
                        fontSize: 12.5,
                        fontWeight: 600,
                        textDecoration: 'none',
                        textAlign: 'center',
                        marginTop: 4,
                      }}
                    >
                      عرض التقارير
                    </NavLink>
                  </div>
                </div>
              )}
            </div>
          </div>

          {/* Right side: add button + icons + avatar */}
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>

            {/* Add New Button */}
            <NavLink
              to="/invoices/new"
              id="add-new-btn"
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: 6,
                padding: '8px 18px',
                background: 'var(--clr-primary)',
                color: '#fff',
                borderRadius: 'var(--radius-pill)',
                fontSize: 13,
                fontWeight: 600,
                textDecoration: 'none',
                boxShadow: '0 2px 8px rgba(92,84,255,0.3)',
                transition: 'all var(--transition-fast)',
                whiteSpace: 'nowrap',
              }}
              onMouseEnter={(e) => {
                (e.currentTarget as HTMLElement).style.background = 'var(--clr-primary-dark)';
                (e.currentTarget as HTMLElement).style.boxShadow = '0 4px 14px rgba(92,84,255,0.4)';
              }}
              onMouseLeave={(e) => {
                (e.currentTarget as HTMLElement).style.background = 'var(--clr-primary)';
                (e.currentTarget as HTMLElement).style.boxShadow = '0 2px 8px rgba(92,84,255,0.3)';
              }}
            >
              <span style={{ fontSize: 16, lineHeight: 1 }}>＋</span>
              إضافة جديد
            </NavLink>

            {/* Notifications */}
            <button
              type="button"
              id="notifications-btn"
              className="btn-icon"
              title="الإشعارات"
              style={{ position: 'relative' }}
            >
              🔔
              <span style={{
                position: 'absolute',
                top: 6,
                right: 6,
                width: 8,
                height: 8,
                background: 'var(--clr-danger)',
                borderRadius: '50%',
                border: '2px solid #fff',
              }} />
            </button>

            {/* Messages */}
            <button
              type="button"
              id="messages-btn"
              className="btn-icon"
              title="الرسائل"
              style={{ position: 'relative' }}
            >
              ✉
              <span style={{
                position: 'absolute',
                top: 6,
                right: 6,
                width: 8,
                height: 8,
                background: 'var(--clr-primary)',
                borderRadius: '50%',
                border: '2px solid #fff',
              }} />
            </button>

            {/* Divider */}
            <div style={{ width: 1, height: 24, background: 'var(--clr-border)', margin: '0 4px' }} />

            {/* User Avatar + Dropdown */}
            <div ref={userRef} style={{ position: 'relative' }}>
              <button
                type="button"
                id="user-avatar-btn"
                onClick={() => setUserMenuOpen((v) => !v)}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                  padding: '6px 10px 6px 6px',
                  background: 'transparent',
                  border: 'none',
                  borderRadius: 'var(--radius-pill)',
                  cursor: 'pointer',
                  transition: 'background var(--transition-fast)',
                }}
                onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.background = 'var(--clr-surface-2)'; }}
                onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.background = 'transparent'; }}
              >
                <div style={{
                  width: 34,
                  height: 34,
                  borderRadius: '50%',
                  background: 'linear-gradient(135deg, #5c54ff, #7b75ff)',
                  color: '#fff',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  fontSize: 13,
                  fontWeight: 700,
                  flexShrink: 0,
                  boxShadow: '0 2px 8px rgba(92,84,255,0.3)',
                }}>
                  {initials}
                </div>
                <div style={{ textAlign: 'right' }}>
                  <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--txt-primary)', lineHeight: 1.2 }}>
                    {displayName.split(' ')[0]}
                  </div>
                  <div style={{ fontSize: 11, color: 'var(--txt-muted)' }}>مسؤول</div>
                </div>
                <span style={{ fontSize: 10, color: 'var(--txt-muted)' }}>▼</span>
              </button>

              {/* User Dropdown */}
              {userMenuOpen && (
                <div
                  id="user-menu-panel"
                  style={{
                    position: 'absolute',
                    top: 'calc(100% + 8px)',
                    left: 0,
                    width: 200,
                    background: 'var(--clr-surface)',
                    borderRadius: 'var(--radius-md)',
                    boxShadow: '0 8px 32px rgba(0,0,0,0.12)',
                    border: '1px solid var(--clr-border)',
                    overflow: 'hidden',
                    zIndex: 200,
                    animation: 'fadeIn 0.12s ease',
                  }}
                >
                  <div style={{ padding: '14px 16px', borderBottom: '1px solid var(--clr-border)' }}>
                    <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--txt-primary)' }}>{displayName}</div>
                    <div style={{ fontSize: 12, color: 'var(--txt-muted)', marginTop: 2 }}>admin@autoparts.local</div>
                  </div>
                  <div style={{ padding: 8 }}>
                    <button
                      type="button"
                      onClick={() => { logout(); navigate('/login'); }}
                      style={{
                        width: '100%',
                        display: 'flex',
                        alignItems: 'center',
                        gap: 8,
                        padding: '9px 12px',
                        background: 'transparent',
                        border: 'none',
                        borderRadius: 'var(--radius-sm)',
                        fontSize: 13,
                        color: 'var(--clr-danger)',
                        cursor: 'pointer',
                        fontWeight: 500,
                        textAlign: 'right',
                        transition: 'background var(--transition-fast)',
                      }}
                      onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.background = 'var(--clr-danger-light)'; }}
                      onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.background = 'transparent'; }}
                    >
                      <span>↩</span>
                      تسجيل الخروج
                    </button>
                  </div>
                </div>
              )}
            </div>
          </div>
        </header>

        {/* ────────── PAGE CONTENT ────────── */}
        <div style={{
          padding: 'var(--content-padding)',
          flex: 1,
          overflowY: 'auto',
          overflowX: 'hidden',
        }}>
          <Outlet />
        </div>
      </main>
    </div>
  );
}

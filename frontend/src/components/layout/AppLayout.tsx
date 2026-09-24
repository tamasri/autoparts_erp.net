/** Application shell: menu on the right (RTL), top bar with quick actions and the user menu, page content in the middle. */
import { useState } from 'react';
import { Link as RouterLink, NavLink, Outlet, matchPath, useLocation, useNavigate } from 'react-router-dom';
import {
  AppBar, Avatar, Badge, Box, Button, Divider, IconButton, List, ListItemButton, ListItemIcon, ListItemText, ListSubheader, Menu, MenuItem, Tab, Tabs, ToggleButton, ToggleButtonGroup, Toolbar, Tooltip, Typography,
  useMediaQuery,
} from '@mui/material';
import { alpha, useTheme } from '@mui/material/styles';
import { patternImage } from '../../theme/pattern';
import { useAuthStore } from '../../stores/authStore';
import { signOut } from '../../lib/session';
import { useDisplayStore, type PrimaryCurrency } from '../../stores/displayStore';
import { NAV_GROUPS, QUICK_ACTIONS, type NavItem, type NavTab } from './navigation';
import { useRealtime } from '../../hooks/useRealtime';
import { useRealtimeStore } from '../../stores/realtimeStore';

const WIDTH = 260;
const WIDTH_COLLAPSED = 68;

const tabMatches = (tab: NavTab, pathname: string): boolean => matchPath({ path: tab.to, end: tab.end ?? true }, pathname) !== null;
const itemMatches = (item: NavItem, pathname: string): boolean =>
  item.tabs ? item.tabs.some((t) => tabMatches(t, pathname)) : matchPath({ path: item.to, end: item.end ?? false }, pathname) !== null;

const initials = (name: string): string => {
  const parts = name.trim().split(/\s+/);
  return (parts.length >= 2 ? parts[0][0] + parts[1][0] : name.slice(0, 2)).toUpperCase();
};

function Sidebar({ collapsed, onToggle }: { collapsed: boolean; onToggle: () => void }): JSX.Element {
  const theme = useTheme();
  const { brand } = theme.palette;
  const { pathname } = useLocation();
  // Live counts on the menu: requests waiting for this user, open stock alerts (a section counts its tabs).
  const pendingApprovals = useRealtimeStore((st) => st.pendingApprovals);
  const openStockAlerts = useRealtimeStore((st) => st.openStockAlerts);
  const countFor = (to: string): number => (to === '/approvals' ? pendingApprovals : to === '/inventory/alerts' ? openStockAlerts : 0);
  const badgeOf = (item: NavItem): number => [item.to, ...(item.tabs ?? []).map((t) => t.to)].reduce((n, to) => n + countFor(to), 0);
  return (
    <Box
      component="aside"
      sx={{
        width: collapsed ? WIDTH_COLLAPSED : WIDTH, flexShrink: 0, color: brand.stone, position: 'sticky', top: 0, height: '100vh',
        display: 'flex', flexDirection: 'column', overflow: 'hidden', transition: 'width 160ms ease', zIndex: 10,
        // A white menu over the identity pattern, separated from the page by a thin line.
        bgcolor: 'background.paper', backgroundImage: patternImage(brand.sand, 0.16, 1), backgroundSize: '72px 72px',
        borderInlineEnd: 1, borderColor: 'divider',
      }}
    >
      <Box
        sx={{
          height: 64, px: 2, display: 'flex', alignItems: 'center', gap: 1.5, borderBottom: 1, borderColor: 'divider', flexShrink: 0,
          // The brand band: the pattern at full strength.
          bgcolor: 'background.paper', backgroundImage: patternImage(brand.wheat, 0.28, 1.2), backgroundSize: '48px 48px',
        }}
      >
        <Avatar variant="rounded" sx={{ bgcolor: brand.emerald, color: brand.sand, fontWeight: 800, width: 36, height: 36 }}>A</Avatar>
        {!collapsed ? <Typography fontWeight={800} color={brand.forest} noWrap sx={{ bgcolor: alpha('#fff', 0.85), px: 0.75, borderRadius: 1 }}>AutoParts ERP</Typography> : null}
      </Box>

      <Box component="nav" sx={{ flex: 1, overflowY: 'auto', overflowX: 'hidden', py: 1 }}>
        {NAV_GROUPS.map((group) => (
          <List
            key={group.title}
            dense
            disablePadding
            subheader={collapsed ? <Divider sx={{ my: 1 }} /> : (
              <ListSubheader disableSticky sx={{ bgcolor: 'transparent', color: brand.wheat, fontSize: 11, fontWeight: 700, lineHeight: '30px', mt: 1 }}>{group.title}</ListSubheader>
            )}
          >
            {group.items.map((item) => (
              <Tooltip key={item.to} title={collapsed ? item.label : ''} placement="left">
                <ListItemButton
                  component={RouterLink}
                  to={item.to}
                  selected={itemMatches(item, pathname)}
                  sx={{
                    mx: 1, my: 0.25, borderRadius: 2, minHeight: 40, color: 'inherit', justifyContent: collapsed ? 'center' : 'flex-start',
                    // Selected: tinted, with an emerald bar on the reading edge (written LTR; the RTL cache flips it).
                    '&.Mui-selected': { bgcolor: brand.primaryTint, color: brand.forest, fontWeight: 700, boxShadow: `inset 3px 0 0 ${brand.emerald}` },
                    '&.Mui-selected:hover': { bgcolor: alpha(brand.emerald, 0.14) },
                    '&:hover': { bgcolor: alpha(brand.emerald, 0.06) },
                  }}
                >
                  <ListItemIcon sx={{ color: 'inherit', minWidth: collapsed ? 0 : 34, justifyContent: 'center', fontSize: 16 }}>
                    <Badge badgeContent={badgeOf(item)} color="error" max={99} overlap="circular">{item.icon}</Badge>
                  </ListItemIcon>
                  {!collapsed ? <ListItemText primary={item.label} primaryTypographyProps={{ fontSize: 14, noWrap: true }} /> : null}
                </ListItemButton>
              </Tooltip>
            ))}
          </List>
        ))}
      </Box>

      <Box sx={{ p: 1, borderTop: 1, borderColor: 'divider', bgcolor: 'background.paper' }}>
        <Button fullWidth size="small" onClick={onToggle} sx={{ color: 'text.secondary' }}>{collapsed ? '»' : '« طيّ القائمة'}</Button>
      </Box>
    </Box>
  );
}

export default function AppLayout(): JSX.Element {
  useRealtime();
  const theme = useTheme();
  const narrow = useMediaQuery(theme.breakpoints.down('md'));
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const [collapsedByUser, setCollapsedByUser] = useState(false);
  const primary = useDisplayStore((st) => st.primary);
  const setPrimary = useDisplayStore((st) => st.setPrimary);
  const [quick, setQuick] = useState<HTMLElement | null>(null);
  const [account, setAccount] = useState<HTMLElement | null>(null);
  const { pathname } = useLocation();
  const section = NAV_GROUPS.flatMap((g) => g.items).find((i) => i.tabs && itemMatches(i, pathname));

  const collapsed = narrow || collapsedByUser;
  const name = user?.fullName || user?.username || 'مستخدم';

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh' }}>
      <Sidebar collapsed={collapsed} onToggle={() => setCollapsedByUser((v) => !v)} />

      <Box sx={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}>
        <AppBar position="sticky" color="inherit" elevation={0} sx={{ borderBottom: 1, borderColor: 'divider', bgcolor: 'background.paper' }}>
          <Toolbar sx={{ gap: 1.5, minHeight: 64 }}>
            <Button variant="contained" size="small" onClick={(e) => setQuick(e.currentTarget)} sx={{ borderRadius: 5 }}>＋ إضافة جديد</Button>
            <Menu anchorEl={quick} open={Boolean(quick)} onClose={() => setQuick(null)}>
              {QUICK_ACTIONS.map((a) => <MenuItem key={a.to} onClick={() => { setQuick(null); navigate(a.to); }}>{a.label}</MenuItem>)}
            </Menu>

            <Box sx={{ flex: 1 }} />

            {/* Which currency amounts are shown in first (display only; everything is kept in dollars). */}
            <Tooltip title="عملة العرض الأساسية">
              <ToggleButtonGroup size="small" exclusive value={primary} onChange={(_, v: PrimaryCurrency | null) => { if (v) setPrimary(v); }}>
                <ToggleButton value="SYP" sx={{ px: 1.25 }}>ل.س</ToggleButton>
                <ToggleButton value="USD" sx={{ px: 1.25 }}>$</ToggleButton>
              </ToggleButtonGroup>
            </Tooltip>

            <Box sx={{ textAlign: 'end', display: { xs: 'none', sm: 'block' } }}>
              <Typography variant="body2" fontWeight={700} lineHeight={1.2}>{name}</Typography>
              <Typography variant="caption" color="text.secondary">{user?.roles?.[0] ?? ''}</Typography>
            </Box>
            <IconButton onClick={(e) => setAccount(e.currentTarget)} size="small">
              <Avatar sx={{ bgcolor: 'primary.main', color: theme.palette.brand.sand, width: 36, height: 36, fontSize: 14, fontWeight: 700 }}>{initials(name)}</Avatar>
            </IconButton>
            <Menu anchorEl={account} open={Boolean(account)} onClose={() => setAccount(null)}>
              <MenuItem disabled>{name}</MenuItem>
              <Divider />
              <MenuItem onClick={() => { setAccount(null); void signOut().then(() => navigate('/login')); }}>تسجيل الخروج</MenuItem>
            </Menu>
          </Toolbar>
        </AppBar>

        <Box component="main" sx={{ flex: 1, p: { xs: 2, md: 3 }, minWidth: 0 }}>
          {section?.tabs ? (
            <Tabs
              value={Math.max(0, section.tabs.findIndex((t) => tabMatches(t, pathname)))} variant="scrollable" scrollButtons="auto"
              sx={{ mb: 3, borderBottom: 1, borderColor: 'divider' }}
            >
              {section.tabs.map((t) => <Tab key={t.to} label={t.label} component={NavLink} to={t.to} end={t.end ?? true} />)}
            </Tabs>
          ) : null}
          <Outlet />
        </Box>
      </Box>
    </Box>
  );
}

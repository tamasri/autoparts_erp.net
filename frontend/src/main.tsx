import { CacheProvider } from '@emotion/react';
import createCache from '@emotion/cache';
import { Box, Container, CssBaseline, Link, Stack, ThemeProvider, Typography } from '@mui/material';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import ReactDOM from 'react-dom/client';
import { I18nextProvider, useTranslation } from 'react-i18next';
import { BrowserRouter, Link as RouterLink, Navigate, Route, Routes, useLocation } from 'react-router-dom';
import rtlPlugin from 'stylis-plugin-rtl';
import { appTheme } from './theme';
import { i18n } from './i18n';
import { ApprovalsPage } from './pages/Approvals';
import { AuditLogPage } from './pages/AuditLog';
import { DashboardPage } from './pages/Dashboard';
import Login from './pages/Login';
import { PeriodLocksPage } from './pages/PeriodLocks';
import { RolesPage } from './pages/Roles';
import { UsersPage } from './pages/Users';
import { PartiesPage } from './pages/parties/Parties';
import { PartyCreatePage } from './pages/parties/PartyCreate';
import { PartyProfilePage } from './pages/parties/PartyProfile';
import { useErpHub } from './hooks/useErpHub';
import { useAuthStore } from './stores/authStore';

const queryClient = new QueryClient();
const rtlCache = createCache({
  key: 'mui-rtl',
  stylisPlugins: [rtlPlugin],
});

function RequireAuth({ children }: { children: JSX.Element }): JSX.Element {
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  const location = useLocation();

  if (!isAuthenticated) {
    return <Navigate to='/login' replace state={{ from: location.pathname }} />;
  }

  return children;
}

function AppShell(): JSX.Element {
  const { t } = useTranslation();
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);
  useErpHub(isAuthenticated);

  return (
    <Container maxWidth='lg' sx={{ py: 4 }}>
      <Stack spacing={3}>
        <Box
          sx={{
            p: 3,
            borderRadius: 3,
            background: 'linear-gradient(120deg, #0f766e 0%, #14532d 100%)',
            color: '#ffffff',
          }}
        >
          <Typography variant='h4'>{t('appName')}</Typography>
          <Stack direction='row' spacing={2} sx={{ mt: 2, flexWrap: 'wrap' }}>
            <Link component={RouterLink} color='inherit' to='/login' underline='hover'>
              {t('nav.login')}
            </Link>
            <Link component={RouterLink} color='inherit' to='/dashboard' underline='hover'>
              {t('nav.dashboard')}
            </Link>
            <Link component={RouterLink} color='inherit' to='/users' underline='hover'>
              {t('nav.users')}
            </Link>
            <Link component={RouterLink} color='inherit' to='/roles' underline='hover'>
              {t('nav.roles')}
            </Link>
            <Link component={RouterLink} color='inherit' to='/approvals' underline='hover'>
              {t('nav.approvals')}
            </Link>
            <Link component={RouterLink} color='inherit' to='/period-locks' underline='hover'>
              {t('nav.periodLocks')}
            </Link>
            <Link component={RouterLink} color='inherit' to='/audit' underline='hover'>
              {t('nav.audit')}
            </Link>
            <Link component={RouterLink} color='inherit' to='/parties' underline='hover'>
              {t('nav.parties')}
            </Link>
          </Stack>
        </Box>

        <Routes>
          <Route path='/' element={<Navigate to={isAuthenticated ? '/dashboard' : '/login'} replace />} />
          <Route path='/login' element={isAuthenticated ? <Navigate to='/dashboard' replace /> : <Login />} />
          <Route path='/dashboard' element={<RequireAuth><DashboardPage /></RequireAuth>} />
          <Route path='/users' element={<RequireAuth><UsersPage /></RequireAuth>} />
          <Route path='/roles' element={<RequireAuth><RolesPage /></RequireAuth>} />
          <Route path='/approvals' element={<RequireAuth><ApprovalsPage /></RequireAuth>} />
          <Route path='/period-locks' element={<RequireAuth><PeriodLocksPage /></RequireAuth>} />
          <Route path='/audit' element={<RequireAuth><AuditLogPage /></RequireAuth>} />
          <Route path='/parties' element={<RequireAuth><PartiesPage /></RequireAuth>} />
          <Route path='/parties/new' element={<RequireAuth><PartyCreatePage /></RequireAuth>} />
          <Route path='/parties/:id' element={<RequireAuth><PartyProfilePage /></RequireAuth>} />
        </Routes>
      </Stack>
    </Container>
  );
}

const rootElement = document.getElementById('root');

if (!rootElement) {
  throw new Error('Root element not found.');
}

ReactDOM.createRoot(rootElement).render(
  <React.StrictMode>
    <CacheProvider value={rtlCache}>
      <ThemeProvider theme={appTheme}>
        <CssBaseline />
        <I18nextProvider i18n={i18n}>
          <QueryClientProvider client={queryClient}>
            <BrowserRouter>
              <AppShell />
            </BrowserRouter>
          </QueryClientProvider>
        </I18nextProvider>
      </ThemeProvider>
    </CacheProvider>
  </React.StrictMode>,
);

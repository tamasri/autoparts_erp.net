/**
 * App.tsx — AutoPartsERP
 *
 * Phase 6: React.lazy per route (code-splitting) + Suspense + ErrorBoundary.
 *
 * Each route is lazy-loaded. The login screen no longer ships the entire ERP
 * bundle. ErrorBoundary catches per-route render errors so one broken screen
 * does not crash the whole app.
 *
 * feat(frontend): React.lazy + ErrorBoundary on all routes (phase6)
 */
import { lazy, Suspense } from 'react';
import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { Box, CircularProgress } from '@mui/material';
import { useAuthStore } from './stores/authStore';
import { ErrorBoundary } from './components/common/ErrorBoundary';

// ── Lazy route imports ────────────────────────────────────────────────────────
const Login            = lazy(() => import('./pages/Login'));
const AppLayout        = lazy(() => import('./components/layout/AppLayout'));
const Dashboard        = lazy(() => import('./pages/Dashboard'));
const KpiDashboard     = lazy(() => import('./pages/kpi/KpiDashboard'));
const Accounts         = lazy(() => import('./pages/accounts/Accounts'));
const CustomerDetail   = lazy(() => import('./pages/customers/CustomerDetail'));
const Invoices         = lazy(() => import('./pages/invoices/Invoices'));
const Payments         = lazy(() => import('./pages/sales/Payments'));
const InvoiceWorkspace = lazy(() => import('./pages/invoices/InvoiceWorkspace'));
const InvoiceDetail    = lazy(() => import('./pages/invoices/InvoiceDetail'));
const FxRates          = lazy(() => import('./pages/sales/FxRates'));
const Inventory        = lazy(() => import('./pages/inventory/Inventory'));
const Receiving        = lazy(() => import('./pages/inventory/Receiving'));
const Transfers        = lazy(() => import('./pages/inventory/Transfers'));
const CycleCounts      = lazy(() => import('./pages/inventory/CycleCounts'));
const StockAdjustments = lazy(() => import('./pages/inventory/StockAdjustments'));
const IssueOrders      = lazy(() => import('./pages/inventory/IssueOrders'));
const Warehouses       = lazy(() => import('./pages/inventory/Warehouses'));
const Movements        = lazy(() => import('./pages/inventory/Movements'));
const InventoryAlerts  = lazy(() => import('./pages/inventory/InventoryAlerts'));
const CombinedStatement= lazy(() => import('./pages/parties/CombinedStatement'));
const Users            = lazy(() => import('./pages/settings/Users'));
const Roles            = lazy(() => import('./pages/settings/Roles'));
const Approvals        = lazy(() => import('./pages/approvals/Approvals'));
const AuditLog         = lazy(() => import('./pages/audit/AuditLog'));
const Items            = lazy(() => import('./pages/items/Items'));
const ItemCard         = lazy(() => import('./pages/items/ItemCard'));
const AccountingSync   = lazy(() => import('./pages/accounting/AccountingSync'));
const ChartOfAccounts  = lazy(() => import('./pages/accounting/ChartOfAccounts'));
const ErpDocuments     = lazy(() => import('./pages/accounting/ErpDocuments'));
const PeriodLocks      = lazy(() => import('./pages/periods/PeriodLocks'));

// ── Page loading fallback ─────────────────────────────────────────────────────
function PageLoader(): JSX.Element {
  return (
    <Box
      sx={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        minHeight: '60vh',
      }}
    >
      <CircularProgress size={40} />
    </Box>
  );
}

// ── Auth guard ────────────────────────────────────────────────────────────────
function PrivateRoute({ children }: { children: JSX.Element }): JSX.Element {
  const token = useAuthStore((s) => s.token);
  return token ? children : <Navigate to="/login" replace />;
}

// ── Route wrapper: ErrorBoundary + Suspense ───────────────────────────────────
function RouteWrapper({ children }: { children: JSX.Element }): JSX.Element {
  // Keyed by the URL so a crash on one screen does not stick to the next one the user opens.
  const { pathname } = useLocation();
  return (
    <ErrorBoundary key={pathname}>
      <Suspense fallback={<PageLoader />}>
        {children}
      </Suspense>
    </ErrorBoundary>
  );
}

// ── App ───────────────────────────────────────────────────────────────────────
export default function App(): JSX.Element {
  return (
    <Routes>
      <Route
        path="/login"
        element={
          <RouteWrapper>
            <Login />
          </RouteWrapper>
        }
      />
      <Route
        path="/"
        element={
          <PrivateRoute>
            <RouteWrapper>
              <AppLayout />
            </RouteWrapper>
          </PrivateRoute>
        }
      >
        <Route index                          element={<RouteWrapper><Dashboard /></RouteWrapper>} />
        <Route path="dashboard"               element={<RouteWrapper><Dashboard /></RouteWrapper>} />
        <Route path="kpi"                     element={<RouteWrapper><KpiDashboard /></RouteWrapper>} />
        <Route path="accounts"                element={<RouteWrapper><Accounts /></RouteWrapper>} />
        <Route path="customers"               element={<Navigate to="/accounts?tab=customers" replace />} />
        <Route path="customers/:id"           element={<RouteWrapper><CustomerDetail /></RouteWrapper>} />
        <Route path="invoices"                element={<RouteWrapper><Invoices /></RouteWrapper>} />
        <Route path="payments"                element={<RouteWrapper><Payments /></RouteWrapper>} />
        <Route path="invoices/new"            element={<RouteWrapper><InvoiceWorkspace /></RouteWrapper>} />
        <Route path="invoices/:id"            element={<RouteWrapper><InvoiceDetail /></RouteWrapper>} />
        <Route path="fx-rates"                element={<RouteWrapper><FxRates /></RouteWrapper>} />
        <Route path="inventory"               element={<RouteWrapper><Inventory /></RouteWrapper>} />
        <Route path="inventory/receiving"     element={<RouteWrapper><Receiving /></RouteWrapper>} />
        <Route path="inventory/transfers"     element={<RouteWrapper><Transfers /></RouteWrapper>} />
        <Route path="inventory/cycle-counts"  element={<RouteWrapper><CycleCounts /></RouteWrapper>} />
        <Route path="inventory/adjustments"   element={<RouteWrapper><StockAdjustments /></RouteWrapper>} />
        <Route path="inventory/issue-orders"  element={<RouteWrapper><IssueOrders /></RouteWrapper>} />
        <Route path="inventory/warehouses"    element={<RouteWrapper><Warehouses /></RouteWrapper>} />

        <Route path="inventory/movements"      element={<RouteWrapper><Movements /></RouteWrapper>} />

        <Route path="inventory/alerts"        element={<RouteWrapper><InventoryAlerts /></RouteWrapper>} />
        <Route path="items"                   element={<RouteWrapper><Items /></RouteWrapper>} />
        <Route path="items/:id"               element={<RouteWrapper><ItemCard /></RouteWrapper>} />
        <Route path="parties"                 element={<Navigate to="/accounts" replace />} />
        <Route path="parties/:id/statement"   element={<RouteWrapper><CombinedStatement /></RouteWrapper>} />
        <Route path="approvals"               element={<RouteWrapper><Approvals /></RouteWrapper>} />
        <Route path="audit"                   element={<RouteWrapper><AuditLog /></RouteWrapper>} />
        <Route path="accounting/sync"         element={<RouteWrapper><AccountingSync /></RouteWrapper>} />

        <Route path="accounting/chart"           element={<RouteWrapper><ChartOfAccounts /></RouteWrapper>} />

        <Route path="accounting/erp-documents"   element={<RouteWrapper><ErpDocuments /></RouteWrapper>} />
        <Route path="periods"                 element={<RouteWrapper><PeriodLocks /></RouteWrapper>} />
        <Route path="users"                   element={<RouteWrapper><Users /></RouteWrapper>} />
        <Route path="roles"                   element={<RouteWrapper><Roles /></RouteWrapper>} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

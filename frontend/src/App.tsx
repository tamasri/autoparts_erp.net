import { Navigate, Route, Routes } from 'react-router-dom';
import { useAuthStore } from './stores/authStore';
import Login from './pages/Login';
import AppLayout from './components/layout/AppLayout';
import Dashboard from './pages/Dashboard';
import Customers from './pages/customers/Customers';
import CustomerDetail from './pages/customers/CustomerDetail';
import Invoices from './pages/invoices/Invoices';
import InvoiceWorkspace from './pages/invoices/InvoiceWorkspace';
import InvoiceDetail from './pages/invoices/InvoiceDetail';
import Inventory from './pages/inventory/Inventory';
import Receiving from './pages/inventory/Receiving';
import Transfers from './pages/inventory/Transfers';
import CycleCounts from './pages/inventory/CycleCounts';
import StockAdjustments from './pages/inventory/StockAdjustments';
import IssueOrders from './pages/inventory/IssueOrders';
import InventoryAlerts from './pages/inventory/InventoryAlerts';
import Parties from './pages/parties/Parties';
import CombinedStatement from './pages/parties/CombinedStatement';
import Users from './pages/settings/Users';
import Roles from './pages/settings/Roles';
import Approvals from './pages/approvals/Approvals';
import AuditLog from './pages/audit/AuditLog';
import PeriodLocks from './pages/periods/PeriodLocks';

function PrivateRoute({ children }: { children: JSX.Element }): JSX.Element {
  const token = useAuthStore((s) => s.token);
  return token ? children : <Navigate to="/login" replace />;
}

export default function App(): JSX.Element {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route
        path="/"
        element={(
          <PrivateRoute>
            <AppLayout />
          </PrivateRoute>
        )}
      >
        <Route index element={<Dashboard />} />
        <Route path="dashboard" element={<Dashboard />} />
        <Route path="customers" element={<Customers />} />
        <Route path="customers/:id" element={<CustomerDetail />} />
        <Route path="invoices" element={<Invoices />} />
        <Route path="invoices/new" element={<InvoiceWorkspace />} />
        <Route path="invoices/:id" element={<InvoiceDetail />} />
        <Route path="inventory" element={<Inventory />} />
        <Route path="inventory/receiving" element={<Receiving />} />
        <Route path="inventory/transfers" element={<Transfers />} />
        <Route path="inventory/cycle-counts" element={<CycleCounts />} />
        <Route path="inventory/adjustments" element={<StockAdjustments />} />
        <Route path="inventory/issue-orders" element={<IssueOrders />} />
        <Route path="inventory/alerts" element={<InventoryAlerts />} />
        <Route path="parties" element={<Parties />} />
        <Route path="parties/:id/statement" element={<CombinedStatement />} />
        <Route path="approvals" element={<Approvals />} />
        <Route path="audit" element={<AuditLog />} />
        <Route path="periods" element={<PeriodLocks />} />
        <Route path="users" element={<Users />} />
        <Route path="roles" element={<Roles />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

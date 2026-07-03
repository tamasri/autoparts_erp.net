import { Navigate, Route, Routes } from 'react-router-dom';
import { useAuthStore } from './stores/authStore';
import Login from './pages/Login';
import AppLayout from './components/layout/AppLayout';
import Dashboard from './pages/Dashboard';
import Customers from './pages/customers/Customers';
import CustomerDetail from './pages/customers/CustomerDetail';
import Invoices from './pages/invoices/Invoices';
import InvoiceDetail from './pages/invoices/InvoiceDetail';
import Inventory from './pages/inventory/Inventory';
import Parties from './pages/parties/Parties';
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
        <Route path="invoices/:id" element={<InvoiceDetail />} />
        <Route path="inventory" element={<Inventory />} />
        <Route path="parties" element={<Parties />} />
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

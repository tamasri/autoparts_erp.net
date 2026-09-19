import { apiClient } from '../client';

export type SalesDay = { day: string; totalSyp: number; totalUsd: number; invoiceCount: number };
export type TopCustomer = { customerId: string; customerName: string; totalSyp: number; totalUsd: number; invoiceCount: number };
export type RecentInvoice = { id: string; invoiceNumber: string; customerName: string; invoiceDate: string; totalSyp: number; totalUsd: number; status: string };

export type DashboardSummary = {
  activeCustomers: number;
  postedInvoices: number;
  receivablesSyp: number;
  receivablesUsd: number;
  overdueInvoices: number;
  overdueSyp: number;
  salesTodaySyp: number;
  salesTodayUsd: number;
  salesMonthSyp: number;
  salesMonthUsd: number;
  skusInStock: number;
  skusOutOfStock: number;
  skusLowStock: number;
  openAlerts: number;
  salesByDay: SalesDay[];
  topCustomers: TopCustomer[];
  recentInvoices: RecentInvoice[];
};

export const dashboardApi = {
  getSummary: () => apiClient.get('/dashboard/summary'),
};

import { apiClient } from '../client';

export type RecentInvoice = { id: string; invoiceNumber: string; customerName: string; invoiceDate: string; totalSyp: number; totalUsd: number; status: string };

/** Today's work on the home screen; the business figures come from getKpis. */
export type DashboardSummary = { openAlerts: number; recentInvoices: RecentInvoice[] };

export type KpiAgeing = {
  totalUsd: number; notDueUsd: number; days1To30Usd: number; days31To60Usd: number; days61To90Usd: number; over90Usd: number;
  overdueCount: number; daysSalesOutstanding: number | null;
};
export type KpiRank = { id: string | null; name: string; amountUsd: number; secondary: number; count: number };
export type BusinessKpis = {
  from: string; to: string;
  sales: {
    grossSalesUsd: number; returnsUsd: number; netSalesUsd: number; costUsd: number; grossProfitUsd: number; grossMarginPct: number | null;
    invoiceCount: number; averageInvoiceUsd: number | null; customerCount: number;
  };
  ledger: { incomeUsd: number; expensesUsd: number; netProfitUsd: number } | null;
  ledgerNote: string | null;
  purchases: { totalUsd: number; billCount: number } | null;
  receivables: KpiAgeing;
  payables: KpiAgeing | null;
  cash: { receiptsUsd: number; supplierPaymentsUsd: number | null; netUsd: number | null } | null;
  stock: { valueUsd: number; skusInStock: number; skusOutOfStock: number; skusBelowReorder: number; slowMoverCount: number; slowMoverValueUsd: number } | null;
  months: Array<{ year: number; month: number; netSalesUsd: number; grossProfitUsd: number; purchasesUsd: number; receiptsUsd: number }>;
  topCustomers: KpiRank[];
  topItems: KpiRank[];
  salesByRep: KpiRank[];
  slowMovers: Array<{ skuId: string; code: string; name: string; quantity: number; valueUsd: number; lastSale: string | null }>;
  overdue: Array<{ invoiceId: string; invoiceNumber: string | null; customerName: string; dueDate: string; daysOverdue: number; balanceUsd: number }>;
};
export type KpiFilters = { from: string; to: string; warehouseId?: string; customerId?: string; salesRepId?: string; categoryId?: string };

export const dashboardApi = {
  getSummary: () => apiClient.get('/dashboard/summary'),
  getKpis: (filters: KpiFilters) => apiClient.get('/dashboard/kpis', { params: filters }),
  getCategories: () => apiClient.get('/catalog/categories'),
};

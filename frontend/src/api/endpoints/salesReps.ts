import { apiClient } from '../client';

/**
 * A rep and their figures for the chosen period (posted invoices, dollars). Gross profit = revenue after discounts, without delivery
 * and tax, minus the cost of the goods; the commission is a percentage of it. The target is in net sales. Percentages are null when
 * there is nothing to divide by.
 */
export type SalesRep = {
  userId: string;
  userName: string;
  fullName: string;
  phone?: string | null;
  commissionPct: number;
  monthlyTargetUsd: number;
  isActive: boolean;
  notes?: string | null;
  customerCount: number;
  invoiceCount: number;
  salesUsd: number;
  returnsUsd: number;
  netSalesUsd: number;
  grossProfitUsd: number;
  grossMarginPct: number | null;
  collectedUsd: number;
  outstandingUsd: number;
  commissionUsd: number;
  targetUsd: number;
  achievementPct: number | null;
  averageInvoiceUsd: number;
  returnRatePct: number | null;
  collectionRatePct: number | null;
};

export type SalesRepDetail = {
  rep: SalesRep;
  months: Array<{ year: number; month: number; targetUsd: number; netSalesUsd: number; grossProfitUsd: number; commissionUsd: number; collectedUsd: number }>;
  customers: Array<{ id: string; code: string; name: string; phone?: string | null; outstandingUsd: number; lastInvoiceDate?: string | null }>;
  invoices: Array<{ id: string; invoiceNumber?: string | null; type: string; invoiceDate: string; customerName: string; totalUsd: number; balanceUsd: number; grossProfitUsd: number }>;
};

export type SalesRepCandidate = { userId: string; userName: string; fullName: string };
export type SaveSalesRep = { commissionPct: number; monthlyTargetUsd: number; isActive: boolean; notes?: string };
export type Period = { from?: string; to?: string };

export const salesRepsApi = {
  list: (params: Period & { includeInactive?: boolean }) => apiClient.get('/sales-reps', { params }),
  detail: (userId: string, params: Period) => apiClient.get(`/sales-reps/${userId}`, { params }),
  candidates: () => apiClient.get('/sales-reps/candidates'),
  save: (userId: string, body: SaveSalesRep) => apiClient.put(`/sales-reps/${userId}`, body),
  assignCustomers: (userId: string, customerIds: string[]) => apiClient.post(`/sales-reps/${userId}/customers`, { customerIds }),
};

export const SALES_REPS = { read: 'sales_reps:read', manage: 'sales_reps:manage' } as const;

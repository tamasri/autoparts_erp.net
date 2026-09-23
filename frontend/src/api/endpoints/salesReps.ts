import { apiClient } from '../client';

/** A rep and their figures for the chosen period (posted invoices, dollars). */
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
  collectedUsd: number;
  outstandingUsd: number;
  commissionUsd: number;
  targetUsd: number;
};

export type SalesRepDetail = {
  rep: SalesRep;
  months: Array<{ year: number; month: number; netSalesUsd: number; collectedUsd: number }>;
  customers: Array<{ id: string; code: string; name: string; phone?: string | null; outstandingUsd: number; lastInvoiceDate?: string | null }>;
  invoices: Array<{ id: string; invoiceNumber?: string | null; type: string; invoiceDate: string; customerName: string; totalUsd: number; balanceUsd: number }>;
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

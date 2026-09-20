import { apiClient } from '../client';

const idem = () => ({ headers: { 'Idempotency-Key': crypto.randomUUID() } });

export type PurchaseLineInput = { itemId: string; quantity: number; unitCostUsd: number; discountPct: number };

export type CreatePurchaseInvoice = {
  supplierPartyId: string;
  billDate: string;
  dueDate: string;
  warehouseId: string;
  supplierRef?: string;
  notes?: string;
  lines: PurchaseLineInput[];
};

export type CreateSupplierPayment = {
  supplierPartyId: string;
  paymentDate: string;
  paymentMethod: string;
  amountUsd: number;
  referenceNumber?: string;
  bankName?: string;
  chequeNumber?: string;
  notes?: string;
  allocations: Array<{ purchaseInvoiceId: string; amountUsd: number }>;
};

export type PurchaseInvoiceRow = {
  id: string; billNumber: string; supplierPartyId: string; supplierName: string; supplierRef?: string | null;
  billDate: string; dueDate: string; status: 'DRAFT' | 'POSTED' | 'VOID'; totalUsd: number; paidUsd: number; balanceUsd: number;
};

export type PurchaseInvoiceDetail = {
  invoice: PurchaseInvoiceRow; warehouseId: string; notes?: string | null; voidReason?: string | null; postedAt?: string | null;
  lines: Array<{ id: string; lineNumber: number; itemCode: string; itemName: string; quantity: number; unitCostUsd: number; discountPct: number; lineTotalUsd: number }>;
};

export type SupplierPaymentRow = {
  id: string; paymentNumber: string; supplierPartyId: string; supplierName: string; paymentDate: string; paymentMethod: string;
  amountUsd: number; unallocatedUsd: number; isReversed: boolean;
};

export const purchasingApi = {
  listInvoices: (params: { page: number; pageSize: number; status?: string; supplierPartyId?: string; search?: string; openOnly?: boolean }) =>
    apiClient.get('/purchase-invoices', { params }),
  getInvoice: (id: string) => apiClient.get(`/purchase-invoices/${id}`),
  createInvoice: (body: CreatePurchaseInvoice) => apiClient.post('/purchase-invoices', body, idem()),
  postInvoice: (id: string) => apiClient.post(`/purchase-invoices/${id}/post`, {}),
  voidInvoice: (id: string, reason: string) => apiClient.post(`/purchase-invoices/${id}/void`, { reason }),
  listPayments: (params: { page: number; pageSize: number; supplierPartyId?: string }) => apiClient.get('/supplier-payments', { params }),
  createPayment: (body: CreateSupplierPayment) => apiClient.post('/supplier-payments', body, idem()),
  reversePayment: (id: string, reason: string) => apiClient.post(`/supplier-payments/${id}/reverse`, { reason }),
};

import { apiClient } from '../client';
import type { CreateReturn, DocumentLink } from './returns';

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
  /** Discount on the whole bill: a percentage of the lines or a dollar amount, never both. */
  discountPct?: number;
  discountAmountUsd?: number;
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
  /** A purchase return (مردود مشتريات): a negative document against a bill. */
  isReturn: boolean;
};

export type PurchaseInvoiceDetail = {
  invoice: PurchaseInvoiceRow; warehouseId: string; notes?: string | null; voidReason?: string | null; postedAt?: string | null;
  lines: Array<{ id: string; lineNumber: number; itemCode: string; itemName: string; quantity: number; unitCostUsd: number; discountPct: number; lineTotalUsd: number; returnOfLineId: string | null }>;
  subtotalUsd: number; discountPct: number | null; discountAmountUsd: number;
  /** Credit of returns applied to this bill (+), or given by this return (−). */
  creditAppliedUsd: number;
  returnAgainst: DocumentLink | null;
  returns: DocumentLink[];
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
  returnable: (id: string) => apiClient.get(`/purchase-invoices/${id}/returnable`),
  createReturn: (id: string, body: CreateReturn) => apiClient.post(`/purchase-invoices/${id}/returns`, body, idem()),
  listPayments: (params: { page: number; pageSize: number; supplierPartyId?: string }) => apiClient.get('/supplier-payments', { params }),
  createPayment: (body: CreateSupplierPayment) => apiClient.post('/supplier-payments', body, idem()),
  reversePayment: (id: string, reason: string) => apiClient.post(`/supplier-payments/${id}/reverse`, { reason }),
};

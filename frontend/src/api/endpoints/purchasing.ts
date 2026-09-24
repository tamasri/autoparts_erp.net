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
  /** GOODS (stock lines) or SERVICE (landed-cost charges owed to a supplier, raised by a landed cost voucher). */
  kind: 'GOODS' | 'SERVICE';
};

export type PurchaseInvoiceDetail = {
  invoice: PurchaseInvoiceRow; warehouseId: string; notes?: string | null; voidReason?: string | null; postedAt?: string | null;
  lines: Array<{ id: string; lineNumber: number; itemCode: string; itemName: string; quantity: number; unitCostUsd: number; discountPct: number; lineTotalUsd: number; returnOfLineId: string | null; chargeType: LandedCostChargeType | null }>;
  subtotalUsd: number; discountPct: number | null; discountAmountUsd: number;
  /** Credit of returns applied to this bill (+), or given by this return (−). */
  creditAppliedUsd: number;
  returnAgainst: DocumentLink | null;
  returns: DocumentLink[];
  /** Landed cost vouchers carrying costs on this goods bill, or the voucher that raised this service bill. */
  landedCosts: DocumentLink[];
};

// ------------------------------------------------------------------ landed cost vouchers (قيد رسملة مصاريف الشراء)

export type LandedCostChargeType = 'FREIGHT' | 'CUSTOMS' | 'SHIPPING' | 'INSURANCE' | 'OTHER';
export type SplitMethod = 'VALUE' | 'QTY' | 'EQUAL';

export const CHARGE_LABEL: Record<LandedCostChargeType, string> = { FREIGHT: 'نقل', CUSTOMS: 'جمارك', SHIPPING: 'شحن', INSURANCE: 'تأمين', OTHER: 'أخرى' };
export const SPLIT_LABEL: Record<SplitMethod, string> = { VALUE: 'حسب القيمة', QTY: 'حسب الكمية', EQUAL: 'بالتساوي' };

/** Owed to a supplier (who gets a service bill) or paid at once from a cash/bank account: exactly one of the two. */
export type LandedCostChargeInput = {
  chargeType: LandedCostChargeType; description: string | null; amountUsd: number; splitMethod: SplitMethod;
  supplierPartyId: string | null; paidFromAccount: string | null;
};
export type SaveLandedCost = { voucherDate: string; fxRateId: string | null; notes: string | null; purchaseInvoiceIds: string[]; charges: LandedCostChargeInput[] };

export type LandedCostRow = { id: string; voucherNumber: string; voucherDate: string; status: 'DRAFT' | 'POSTED' | 'VOID'; totalUsd: number; billNumbers: string; chargeCount: number };
export type LandedCostDetail = {
  voucher: LandedCostRow; fxRateId: string | null; notes: string | null; voidReason: string | null; bills: DocumentLink[];
  charges: Array<LandedCostChargeInput & { id: string; lineNumber: number; supplierName: string | null; serviceBill: DocumentLink | null }>;
  lines: Array<{ purchaseInvoiceLineId: string; billNumber: string; itemCode: string; itemName: string; quantity: number; netUnitCostUsd: number;
    allocatedUsd: number; landedUnitCostUsd: number; byCharge: Record<string, number> }>;
  effects: Array<{ skuId: string; itemCode: string; itemName: string; quantity: number; onHand: number; allocatedUsd: number; capitalizedUsd: number;
    expensedUsd: number; costBeforeUsd: number; costAfterUsd: number }>;
  /** A draft: worked out on today's stock and costs. */
  isPreview: boolean; previewProblem: string | null;
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
  listLandedCosts: (params: { page: number; pageSize: number; status?: string; purchaseInvoiceId?: string }) => apiClient.get('/landed-costs', { params }),
  getLandedCost: (id: string) => apiClient.get(`/landed-costs/${id}`),
  createLandedCost: (body: SaveLandedCost) => apiClient.post('/landed-costs', body),
  updateLandedCost: (id: string, body: SaveLandedCost) => apiClient.put(`/landed-costs/${id}`, body),
  postLandedCost: (id: string) => apiClient.post(`/landed-costs/${id}/post`, {}),
  voidLandedCost: (id: string, reason: string) => apiClient.post(`/landed-costs/${id}/void`, { reason }),
  createReturn: (id: string, body: CreateReturn) => apiClient.post(`/purchase-invoices/${id}/returns`, body, idem()),
  listPayments: (params: { page: number; pageSize: number; supplierPartyId?: string }) => apiClient.get('/supplier-payments', { params }),
  createPayment: (body: CreateSupplierPayment) => apiClient.post('/supplier-payments', body, idem()),
  reversePayment: (id: string, reason: string) => apiClient.post(`/supplier-payments/${id}/reverse`, { reason }),
};

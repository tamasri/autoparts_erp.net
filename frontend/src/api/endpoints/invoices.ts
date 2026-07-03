import { apiClient } from '../client';

export type CreateInvoiceLine = {
  skuId: string;
  batchId?: string;
  locationId: string;
  quantity: number;
  unitPriceSyp: number;
  unitPriceUsd: number;
  discountPct: number;
  isPriceOverride: boolean;
  overrideReason?: string;
};

export type CreateInvoice = {
  customerId: string;
  invoiceDate: string; // yyyy-MM-dd
  dueDate: string; // yyyy-MM-dd
  fxRateId: string;
  invoiceType: string;
  salesRepId?: string;
  deliveryFeeSyp: number;
  deliveryFeeUsd: number;
  lines: CreateInvoiceLine[];
};

export const invoicesApi = {
  getInvoices: (params: {
    page?: number;
    pageSize?: number;
    status?: string;
    type?: string;
    customerId?: string;
    searchTerm?: string;
  }) => apiClient.get('/invoices', { params }),
  getInvoiceById: (id: string) => apiClient.get(`/invoices/${id}`),
  createInvoice: (body: CreateInvoice) => apiClient.post('/invoices', body),
  confirm: (id: string) => apiClient.post(`/invoices/${id}/confirm`, {}),
  post: (id: string) => apiClient.post(`/invoices/${id}/post`, {}),
  void: (id: string, reason: string) => apiClient.post(`/invoices/${id}/void`, { reason }),
  getPdf: (id: string) => apiClient.get(`/invoices/${id}/pdf`, { responseType: 'blob' }),
};

import { apiClient } from '../client';

const idem = () => ({ headers: { 'Idempotency-Key': crypto.randomUUID() } });

export type CreatePayment = {
  paymentType: 'RECEIPT' | 'REFUND';
  customerId: string;
  paymentDate: string; // yyyy-MM-dd
  paymentMethod: string;
  amountSyp: number;
  amountUsd: number;
  fxRateId: string;
  referenceNumber?: string;
  bankName?: string;
  chequeNumber?: string;
  chequeDate?: string;
  notes?: string;
};

export type AllocationLine = { invoiceId: string; allocatedSyp: number; allocatedUsd: number };

export const paymentsApi = {
  list: (params: { page: number; pageSize: number; customerId?: string; paymentMethod?: string }) =>
    apiClient.get('/payments', { params }),
  get: (id: string) => apiClient.get(`/payments/${id}`),
  /** The printed receipt voucher (PDF). */
  getPdf: (id: string) => apiClient.get(`/payments/${id}/pdf`, { responseType: 'blob' }),
  create: (body: CreatePayment) => apiClient.post('/payments', body, idem()),
  allocate: (id: string, allocations: AllocationLine[]) => apiClient.post(`/payments/${id}/allocate`, { allocations }, idem()),
  reverse: (id: string, reason: string) => apiClient.post(`/payments/${id}/reverse`, { reason }, idem()),
};

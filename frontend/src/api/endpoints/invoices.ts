import { apiClient } from '../client';

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
};

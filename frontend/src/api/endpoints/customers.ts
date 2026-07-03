import { apiClient } from '../client';

export const customersApi = {
  getCustomers: (params: {
    page?: number;
    pageSize?: number;
    type?: string;
    isActive?: boolean;
    searchTerm?: string;
  }) => apiClient.get('/customers', { params }),
  getCustomerById: (id: string) => apiClient.get(`/customers/${id}`),
  getCustomerStatement: (id: string) => apiClient.get(`/customers/${id}/statement`),
};

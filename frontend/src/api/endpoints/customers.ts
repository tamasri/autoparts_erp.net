import { apiClient } from '../client';

export type CreateCustomer = {
  code: string;
  name: string;
  type: string;
  phone?: string;
  phone2?: string;
  address?: string;
  city?: string;
  creditLimitSyp: number;
  creditLimitUsd: number;
  paymentTermsDays: number;
  assignedSalesRep?: string;
  notes?: string;
};

export type UpdateCustomer = {
  name: string;
  type: string;
  phone?: string;
  phone2?: string;
  address?: string;
  city?: string;
  creditLimitSyp: number;
  creditLimitUsd: number;
  paymentTermsDays: number;
  assignedSalesRep?: string;
  notes?: string;
};

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
  createCustomer: (body: CreateCustomer) => apiClient.post('/customers', body),
  updateCustomer: (id: string, body: UpdateCustomer) => apiClient.put(`/customers/${id}`, body),
  deactivateCustomer: (id: string, reason?: string) =>
    apiClient.delete(`/customers/${id}`, { params: reason ? { reason } : undefined }),
};

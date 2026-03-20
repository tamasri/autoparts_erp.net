import { apiClient } from '../client';

export const reasonCodesApi = {
  getByCategory: (category: string) => apiClient.get('/reason-codes', { params: { category } }),
  create: (payload: unknown) => apiClient.post('/reason-codes', payload),
};

import { apiClient } from '../client';

export const inventoryApi = {
  getStock: (params: {
    page?: number;
    pageSize?: number;
    locationId?: string;
    skuId?: string;
    searchTerm?: string;
  }) => apiClient.get('/inventory/stock', { params }),
  searchItems: (query: string, page = 1, pageSize = 50) =>
    apiClient.get('/items/search', { params: { query, page, pageSize, includeInactive: false } }),
};

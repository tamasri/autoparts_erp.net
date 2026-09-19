import { apiClient } from '../client';

const idem = () => ({ headers: { 'Idempotency-Key': crypto.randomUUID() } });

export type ItemBody = {
  nameEn: string;
  nameAr: string;
  nameArColloquial?: string | null;
  brand?: string | null;
  categoryPath?: string | null;
  hasWarranty: boolean;
  warrantyMonths: number;
  isBatchTracked: boolean;
  reorderLevel: number;
  notes?: string | null;
};

export type CreateItemBody = ItemBody & { partNumber: string; skuId?: string | null };
export type UpdateItemBody = ItemBody & { isActive: boolean };

export type PriceBody = {
  sellingPriceSyp: number;
  sellingPriceUsd: number;
  minSellingPriceSyp: number;
  minSellingPriceUsd: number;
  overrideReason?: string;
  requiresApproval?: boolean;
};

export const itemsApi = {
  browse: (params: { search?: string; page: number; pageSize: number; includeInactive?: boolean }) =>
    apiClient.get('/items', { params: { ...params, search: params.search || undefined } }),
  search: (query: string, pageSize = 8) =>
    apiClient.get('/items/search', { params: { query, page: 1, pageSize, includeInactive: false } }),
  getById: (id: string) => apiClient.get(`/items/${id}`),
  getStock: (id: string) => apiClient.get(`/items/${id}/stock`),
  getAliases: (id: string) => apiClient.get(`/items/${id}/aliases`),
  getInterchanges: (id: string) => apiClient.get(`/items/${id}/interchanges`),
  create: (body: CreateItemBody) => apiClient.post('/items', body, idem()),
  update: (id: string, body: UpdateItemBody) => apiClient.put(`/items/${id}`, body),
  addAlias: (id: string, alias: string) => apiClient.post(`/items/${id}/aliases`, { alias, source: 'MANUAL' }, idem()),
  addInterchange: (id: string, interchangeItemId: string, type: string, priority: number) =>
    apiClient.post(`/items/${id}/interchanges`, { interchangeItemId, type, priority }, idem()),
  stopShip: (id: string, reason: string) => apiClient.post(`/items/${id}/stop-ship`, { reason }, idem()),
  getSku: (skuId: string) => apiClient.get(`/catalog/skus/${skuId}`),
  updatePrices: (skuId: string, body: PriceBody) => apiClient.put(`/catalog/skus/${skuId}/prices`, body),
};

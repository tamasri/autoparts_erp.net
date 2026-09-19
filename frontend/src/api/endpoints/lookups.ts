import { apiClient } from '../client';

export type LocationOption = { id: string; code: string; name: string; type: string; parentId?: string | null };

export type PickStock = { locationId: string; locationCode: string; locationName: string; available: number; reserved: number };
export type PickBatch = { id: string; batchNumber: string; locationId: string; quantity: number; expiryDate?: string | null };

export type PickItem = {
  itemId?: string | null;
  skuId?: string | null;
  code: string;
  name: string;
  nameAr: string;
  brand?: string | null;
  barcode?: string | null;
  isActive: boolean;
  isStopShip: boolean;
  hasWarranty: boolean;
  isBatchTracked: boolean;
  sellingPriceSyp: number;
  sellingPriceUsd: number;
  minSellingPriceSyp: number;
  minSellingPriceUsd: number;
  totalAvailable: number;
  stock: PickStock[];
  batches: PickBatch[];
};

export const lookupsApi = {
  getLocations: (type?: string) => apiClient.get('/locations', { params: { type } }),
  pickItems: (params: { search?: string; mode: 'sales' | 'warehouse'; locationId?: string; inStockOnly?: boolean; page: number; pageSize: number }) =>
    apiClient.get('/inventory/pick', {
      params: { ...params, search: params.search || undefined, locationId: params.locationId || undefined },
    }),
};

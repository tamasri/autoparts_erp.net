import { apiClient } from '../client';

export type LocationOverview = {
  id: string;
  code: string;
  name: string;
  type: string;
  parentId: string | null;
  isActive: boolean;
  skuCount: number;
  totalQty: number;
  valueUsd: number;
  childCount: number;
};

export type StockMovement = {
  id: string;
  createdAt: string;
  itemId: string;
  itemCode: string;
  itemName: string;
  locationId: string;
  locationCode: string;
  movementType: string;
  direction: 'IN' | 'OUT';
  qty: number;
  balanceAfter: number | null;
  referenceType: string | null;
  referenceId: string | null;
  performedBy: string | null;
  notes: string | null;
};

export type MovementFilters = {
  itemId?: string;
  locationId?: string;
  movementType?: string;
  direction?: string;
  from?: string;
  to?: string;
  search?: string;
  page: number;
  pageSize: number;
};

export const warehouseApi = {
  overview: (includeInactive: boolean) => apiClient.get('/locations/overview', { params: { includeInactive } }),
  create: (body: { code: string; name: string; type: string; parentId?: string | null }) => apiClient.post('/locations', body),
  update: (id: string, body: { name: string; type: string; parentId?: string | null; isActive: boolean }) => apiClient.put(`/locations/${id}`, body),
  movements: (f: MovementFilters) =>
    apiClient.get('/inventory/movements', {
      params: {
        itemId: f.itemId || undefined, locationId: f.locationId || undefined, movementType: f.movementType || undefined,
        direction: f.direction || undefined, from: f.from || undefined, to: f.to || undefined, search: f.search || undefined,
        page: f.page, pageSize: f.pageSize,
      },
    }),
};

import { apiClient } from '../client';

export type StockAdjustmentLine = {
  itemId: string;
  locationId: string;
  batchId?: string;
  status: string;
  qtyDelta: number;
  systemQtyBefore: number;
  systemQtyAfter: number;
  notes?: string;
};

export type CreateStockAdjustment = {
  adjustmentType: string;
  warehouseId: string;
  reasonCode: string;
  lines: StockAdjustmentLine[];
};

export const stockAdjustmentsApi = {
  list: (page = 1, pageSize = 20) =>
    apiClient.get('/stock-adjustments', { params: { page, pageSize } }),
  create: (body: CreateStockAdjustment) =>
    apiClient.post('/stock-adjustments', body),
  post: (id: string, operationDate?: string) =>
    apiClient.post(`/stock-adjustments/${id}/post`, {}, {
      params: operationDate ? { operationDate } : undefined,
    }),
};

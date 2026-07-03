import { apiClient } from '../client';

export type TransferOrderLine = {
  itemId: string;
  batchId?: string;
  sourceLocationId?: string;
  destinationLocationId?: string;
  shippedQty: number;
};

export type CreateTransferOrder = {
  sourceWarehouseId: string;
  destinationWarehouseId: string;
  lines: TransferOrderLine[];
};

export type CreateTransferRequest = {
  sourceWarehouseId: string;
  destinationWarehouseId: string;
  lines: TransferOrderLine[];
  notes?: string;
};

export const transfersApi = {
  listRequests: (page = 1, pageSize = 20) =>
    apiClient.get('/transfers/requests', { params: { page, pageSize } }),
  createRequest: (body: CreateTransferRequest) =>
    apiClient.post('/transfers/requests', body),
  listOrders: (page = 1, pageSize = 20) =>
    apiClient.get('/transfers/orders', { params: { page, pageSize } }),
  createOrder: (body: CreateTransferOrder) =>
    apiClient.post('/transfers/orders', body),
  ship: (id: string) =>
    apiClient.post(`/transfers/orders/${id}/ship`, {}),
  receive: (id: string) =>
    apiClient.post(`/transfers/orders/${id}/receive`, {}),
};

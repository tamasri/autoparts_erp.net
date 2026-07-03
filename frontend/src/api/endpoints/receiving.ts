import { apiClient } from '../client';

export type ReceivingLine = {
  itemId: string;
  expectedQty?: number;
  receivedQty: number;
  rejectedQty: number;
  batchId?: string;
  assignedLocationId?: string;
  conditionStatus?: string;
  manufacturerPartMatchOk?: boolean;
  notes?: string;
};

export type CreateReceivingDocument = {
  vendorPartyId?: string;
  purchaseOrderRef?: string;
  warehouseId: string;
  notes?: string;
};

export const receivingApi = {
  list: (page = 1, pageSize = 20) =>
    apiClient.get('/receiving', { params: { page, pageSize } }),
  create: (body: CreateReceivingDocument) =>
    apiClient.post('/receiving', body),
  addLine: (id: string, body: ReceivingLine) =>
    apiClient.post(`/receiving/${id}/lines`, body),
  post: (id: string) =>
    apiClient.post(`/receiving/${id}/post`, {}),
  getPutawayTasks: (id: string) =>
    apiClient.get(`/receiving/${id}/putaway-tasks`),
  completePutaway: (taskId: string, body: { toLocationId: string; qty: number }) =>
    apiClient.post(`/receiving/putaway/${taskId}/complete`, body),
};

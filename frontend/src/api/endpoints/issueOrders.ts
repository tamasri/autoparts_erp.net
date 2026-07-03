import { apiClient } from '../client';

export type IssueOrderLine = {
  itemId: string;
  requestedQty: number;
  sourceLocationId?: string;
  batchId?: string;
};

export type CreateIssueOrder = {
  sourceType: string;
  sourceId?: string;
  warehouseId: string;
  lines: IssueOrderLine[];
  idempotencyKey: string;
};

export const issueOrdersApi = {
  list: (page = 1, pageSize = 20) =>
    apiClient.get('/issue-orders', { params: { page, pageSize } }),
  create: (body: CreateIssueOrder) =>
    apiClient.post('/issue-orders', body),
  generatePickTasks: (id: string) =>
    apiClient.post(`/issue-orders/${id}/pick-tasks/generate`, {}),
  completePick: (id: string, taskId: string) =>
    apiClient.post(`/issue-orders/${id}/pick-tasks/${taskId}/complete`, {}),
  verifyPick: (id: string, taskId: string) =>
    apiClient.post(`/issue-orders/${id}/pick-tasks/${taskId}/verify`, {}),
  issue: (id: string) =>
    apiClient.post(`/issue-orders/${id}/issue`, {}),
};

import { apiClient } from '../client';

export type CreateCycleCountPlan = {
  warehouseId: string;
  scopeType: string;
  scopeFilterJson?: string;
  scheduledFor: string; // yyyy-MM-dd
};

export type RecordCycleCountLine = {
  lineId: string;
  countedQty: number;
};

export const cycleCountsApi = {
  list: (page = 1, pageSize = 20) =>
    apiClient.get('/cycle-counts', { params: { page, pageSize } }),
  create: (body: CreateCycleCountPlan) =>
    apiClient.post('/cycle-counts', body),
  record: (id: string, lines: RecordCycleCountLine[]) =>
    apiClient.post(`/cycle-counts/${id}/record`, { cycleCountPlanId: id, lines }),
  approveVariance: (id: string) =>
    apiClient.post(`/cycle-counts/${id}/approve-variance`, {}),
};

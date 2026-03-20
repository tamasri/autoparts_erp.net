import { apiClient } from '../client';

export const approvalsApi = {
  getPending: (page = 1, pageSize = 20) => apiClient.get('/approvals/pending', { params: { page, pageSize } }),
  approve: (requestId: string, comment?: string) => apiClient.post(`/approvals/${requestId}/approve`, { comment }),
  reject: (requestId: string, comment: string) => apiClient.post(`/approvals/${requestId}/reject`, { comment }),
};

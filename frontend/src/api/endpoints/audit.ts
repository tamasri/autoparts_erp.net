import { apiClient } from '../client';

export const auditApi = {
  getLogs: (params: Record<string, unknown>) => apiClient.get('/audit', { params }),
  getById: (auditLogId: string) => apiClient.get(`/audit/${auditLogId}`),
};

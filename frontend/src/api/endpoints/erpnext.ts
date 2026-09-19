import { apiClient } from '../client';

export const erpnextApi = {
  getSyncLog: (page: number, pageSize: number, status?: string) =>
    apiClient.get('/erpnext/sync-log', { params: { page, pageSize, status: status || undefined } }),
  getSummary: () => apiClient.get('/erpnext/sync-summary'),
  triggerSync: () => apiClient.post('/erpnext/sync'),
};

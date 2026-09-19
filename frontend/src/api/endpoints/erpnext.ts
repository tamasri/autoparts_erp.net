import { apiClient } from '../client';

export const erpnextApi = {
  getSyncLog: () => apiClient.get('/erpnext/sync-log'),
  triggerSync: () => apiClient.post('/erpnext/sync'),
};

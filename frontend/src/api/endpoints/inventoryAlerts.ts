import { apiClient } from '../client';

export const inventoryAlertsApi = {
  list: () => apiClient.get('/inventory/alerts'),
  acknowledge: (id: string) =>
    apiClient.post(`/inventory/alerts/${id}/acknowledge`, {}),
  resolve: (id: string, resolutionNote?: string) =>
    apiClient.post(`/inventory/alerts/${id}/resolve`, { resolutionNote }),
};

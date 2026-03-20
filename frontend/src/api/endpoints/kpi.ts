import { apiClient } from '../client';

export const kpiAdminApi = {
  getDefinitions: () => apiClient.get('/kpi/admin/definitions'),
  createDefinition: (payload: {
    key: string;
    domain: string;
    title: string;
    titleAr: string;
    unit: string;
    direction: 'UP' | 'DOWN';
    description?: string;
  }) => apiClient.post('/kpi/admin/definitions', payload),
  setThreshold: (definitionId: string, payload: {
    warningValue?: number;
    criticalValue?: number;
    effectiveFrom: string;
    effectiveTo?: string;
    reason?: string;
  }) => apiClient.post(`/kpi/admin/definitions/${definitionId}/threshold`, payload),
};

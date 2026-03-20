import { apiClient } from '../client';

export const partiesApi = {
  getParties: (params: {
    page?: number;
    pageSize?: number;
    typeCode?: string;
    isActive?: boolean;
    searchTerm?: string;
    hasCombinedStatement?: boolean;
  }) => apiClient.get('/parties', { params }),

  getPartyById: (partyId: string) => apiClient.get(`/parties/${partyId}`),

  createParty: (payload: {
    displayName: string;
    displayNameAr: string;
    taxNumber?: string;
    website?: string;
    notes?: string;
    initialTypeCodes?: string[];
  }) => apiClient.post('/parties', payload),

  updateParty: (partyId: string, payload: {
    displayName: string;
    displayNameAr: string;
    taxNumber?: string;
    website?: string;
    notes?: string;
    isActive: boolean;
  }) => apiClient.put(`/parties/${partyId}`, payload),

  requestTypeAssignment: (partyId: string, payload: { typeCode: string; reason: string }) =>
    apiClient.post(`/parties/${partyId}/types`, payload),

  deactivateTypeAssignment: (partyId: string, typeCode: string, reason: string) =>
    apiClient.delete(`/parties/${partyId}/types/${typeCode}`, { params: { reason } }),

  getCombinedStatement: (partyId: string) => apiClient.get(`/parties/${partyId}/statement/combined`),

  getArStatement: (partyId: string) => apiClient.get(`/parties/${partyId}/statement/ar`),
};

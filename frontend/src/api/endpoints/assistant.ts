import { apiClient } from '../client';

export type AssistantStatus = {
  enabled: boolean; modelConfigured: boolean; modelName: string;
  /** open | qr | close | starting | offline */
  gatewayState: string; gatewayQr: string | null; gatewayAccount: string | null; gatewayReportedAt: string | null;
};
export type AssistantLink = {
  id: string; userId: string; userName: string; fullName: string; phone: string; status: 'PENDING' | 'ACTIVE' | 'REVOKED';
  userHasAccess: boolean; createdAt: string; verifiedAt: string | null; lastUsedAt: string | null; codeExpiresAt: string | null;
};
export type AssistantLinkCode = { linkId: string; phone: string; code: string; expiresAt: string };

export const assistantApi = {
  status: () => apiClient.get('/assistant/status'),
  links: () => apiClient.get('/assistant/links'),
  createLink: (userId: string, phone: string) => apiClient.post('/assistant/links', { userId, phone }),
  renewCode: (id: string) => apiClient.post(`/assistant/links/${id}/code`),
  revoke: (id: string) => apiClient.post(`/assistant/links/${id}/revoke`),
};

import { apiClient } from '../client';

export type ConsistencyIssue = {
  kind: string; localEntityType?: string | null; localId?: string | null; localRef?: string | null; erpNextName?: string | null;
  localAmount: number | null; erpNextAmount: number | null; detail?: string | null;
};
export type ConsistencySection = {
  key: string; doctype: string; localCount: number; localTotal: number | null; erpNextCount: number; erpNextTotal: number | null; matchedCount: number;
  issueCounts: Record<string, number>; issues: ConsistencyIssue[]; error?: string | null;
};
export type ConsistencyReport = { checkedAt: string; sections: ConsistencySection[] };
export type ReferenceList = { kind: string; doctype: string; columns: string[]; rows: Array<Record<string, string | null>> };

export const erpnextApi = {
  getSyncLog: (page: number, pageSize: number, status?: string) =>
    apiClient.get('/erpnext/sync-log', { params: { page, pageSize, status: status || undefined } }),
  getSummary: () => apiClient.get('/erpnext/sync-summary'),
  triggerSync: () => apiClient.post('/erpnext/sync'),
  consistency: () => apiClient.get('/accounting/erpnext/consistency', { timeout: 120000 }),
  reference: (kind: string) => apiClient.get(`/accounting/erpnext/reference/${kind}`),
};

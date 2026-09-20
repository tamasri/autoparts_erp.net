import { apiClient } from '../client';

export type ErpAccount = {
  name: string;
  accountName: string;
  parentAccount: string | null;
  isGroup: boolean;
  rootType: string | null;
  accountType: string | null;
  currency: string | null;
  balance: number | null;
};

export type ErpMapping = { purpose: string; description: string; account: string | null };

export const erpnextBrowseApi = {
  status: () => apiClient.get('/erpnext/status'),
  accounts: (balances: boolean) => apiClient.get('/erpnext/accounts', { params: { balances }, timeout: 120000 }),
  mapping: () => apiClient.get('/erpnext/accounts/mapping'),
  documents: (doctype: string, page: number, pageSize: number, search: string) =>
    apiClient.get(`/erpnext/documents/${encodeURIComponent(doctype)}`, { params: { page, pageSize, search: search || undefined } }),
};

import { apiClient } from '../client';

/** The numbered kinds of document (the server's NumberedDocuments). The database numbers each series 1, 2, 3 … without gaps. */
export type DocumentKind =
  | 'invoices' | 'payments' | 'purchase-invoices' | 'supplier-payments' | 'journal-entries'
  | 'stock-adjustments' | 'transfer-orders' | 'receiving' | 'issue-orders';

export type DocumentRef = { id: string; serialNo: number; number: string };

export type DocumentNeighbors = {
  seriesCode: string; seriesNameAr: string; serialNo: number; number: string;
  first: DocumentRef | null; previous: DocumentRef | null; next: DocumentRef | null; last: DocumentRef | null;
};

export type DeletedDocument = {
  id: string; seriesCode: string; seriesNameAr: string; serialNo: number; documentNumber: string; documentKind: string; documentId: string;
  statusAtDeletion: string; reason: string; deletedBy: string; deletedByName: string | null; deletedAt: string;
};

export type SeriesHealth = { code: string; prefix: string; nameAr: string; lastNumber: number; live: number; deleted: number; unexplained: number };

/** Only the Super Admin (SYSTEM_ADMIN) holds it; the server refuses it to every other role. */
export const DELETE_DOCUMENTS = 'documents:delete';

/** Kinds the Super Admin may delete, and in which states (posted documents are voided first). */
export const DELETABLE_KINDS: readonly DocumentKind[] = ['invoices', 'purchase-invoices', 'journal-entries'];
export const DELETABLE_STATUSES: readonly string[] = ['DRAFT', 'CONFIRMED', 'VOID'];

export const documentsApi = {
  neighbors: (kind: DocumentKind, id: string) => apiClient.get(`/documents/${kind}/${id}/neighbors`),
  remove: (kind: DocumentKind, id: string, reason: string) => apiClient.delete(`/documents/${kind}/${id}`, { data: { reason } }),
  deleted: (params: { page: number; pageSize: number; series?: string }) => apiClient.get('/documents/deleted', { params }),
  numbering: () => apiClient.get('/documents/numbering'),
};

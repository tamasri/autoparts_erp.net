import { apiClient } from '../client';

// ------------------------------------------------------------------ chart of accounts

export type Account = {
  name: string; accountName: string; parentAccount: string | null; isGroup: boolean;
  rootType: string | null; accountType: string | null; currency: string | null; balance: number | null;
};
export type AccountMapping = { purpose: string; description: string; account: string | null };
export type CreateAccountBody = { accountName: string; parentAccount: string; isGroup: boolean; accountType?: string | null; accountNumber?: string | null };
export type UpdateAccountBody = { accountName?: string; accountType?: string; disabled?: boolean };
export type AccountImportResult = {
  dryRun: boolean; total: number; created: number; existing: number; failed: number;
  rows: Array<{ rowNumber: number; accountName: string; status: 'CREATE' | 'CREATED' | 'EXISTS' | 'ERROR'; message?: string | null }>;
};

// ------------------------------------------------------------------ entries

export type EntryKind = 'RECEIPT' | 'PAYMENT' | 'CONTRA' | 'JOURNAL' | 'OPENING' | 'DEBIT_NOTE' | 'CREDIT_NOTE';
export type EntryType = { id: string; code: string; nameAr: string; kind: EntryKind; prefix: string; description?: string | null; isSystem: boolean; isActive: boolean };
export type SaveEntryTypeBody = { nameAr: string; kind: EntryKind; prefix: string; description?: string | null; isActive: boolean };
export type Tag = { id: string; name: string; color: string };
export type TagTarget = { targetType: 'JOURNAL_ENTRY' | 'ERPNEXT'; targetKey: string };

export type JournalLineInput = { account: string; partyId?: string | null; debit: number; credit: number; narration?: string | null };
export type SaveEntryBody = { entryTypeId: string; entryDate: string; narration?: string | null; referenceNumber?: string | null; lines: JournalLineInput[] };
export type SyncStatus = 'NONE' | 'PENDING' | 'SYNCED' | 'FAILED' | 'SKIPPED' | 'CANCELLED';
export type JournalEntryRow = {
  id: string; entryNumber: string; typeCode: string; typeNameAr: string; kind: EntryKind; entryDate: string; status: 'DRAFT' | 'POSTED' | 'VOID';
  narration?: string | null; totalUsd: number; erpNextName?: string | null; syncStatus: SyncStatus; syncError?: string | null; tags: Tag[];
};
export type JournalEntryDetail = {
  entry: JournalEntryRow; referenceNumber?: string | null; voidReason?: string | null;
  lines: Array<{ lineNumber: number; account: string; partyId?: string | null; partyName?: string | null; debit: number; credit: number; narration?: string | null }>;
};
export type EntryListParams = { page: number; pageSize: number; status?: string; entryTypeId?: string; search?: string; from?: string; to?: string; tagId?: string };

// ------------------------------------------------------------------ reports

export type TrialBalanceLine = { account: string; accountName: string; parent: string | null; depth: number; isGroup: boolean; rootType: string | null; opening: number; debit: number; credit: number; closing: number };
export type TrialBalance = { from: string; to: string; lines: TrialBalanceLine[]; totalDebit: number; totalCredit: number; isBalanced: boolean };
export type StatementLine = { account: string; accountName: string; parent: string | null; depth: number; isGroup: boolean; amount: number };
export type BalanceSheet = {
  asOf: string; assets: StatementLine[]; liabilities: StatementLine[]; equity: StatementLine[];
  netProfit: number; totalAssets: number; totalLiabilities: number; totalEquity: number; isBalanced: boolean;
};
export type ProfitLoss = { from: string; to: string; income: StatementLine[]; expenses: StatementLine[]; totalIncome: number; totalExpenses: number; netProfit: number };
export type LedgerRow = {
  glName: string | null; postingDate: string; voucherType: string | null; voucherNo: string | null; party: string | null; remarks: string | null;
  debit: number; credit: number; balance: number; localEntityType: string | null; localEntityId: string | null; tags: Tag[];
};
/** One page of a statement. Opening, totals and closing cover the whole period; totalCount is the number of lines in it. */
export type LedgerStatement = {
  account: string; rootType: string | null; from: string; to: string; opening: number; rows: LedgerRow[];
  totalDebit: number; totalCredit: number; closing: number; totalCount: number; pageNumber: number; pageSize: number; tagFiltered: boolean;
};
export type LedgerParams = { account: string; from: string; to: string; party?: string; tagId?: string; page?: number; pageSize?: number };
export type PartyBalance = {
  party: string; partyId: string | null; customerId: string | null; balance: number; current: number;
  days1To30: number; days31To60: number; days61To90: number; over90: number; unallocated: number;
};
export type PartyBalances = { partyType: string; asOf: string; rows: PartyBalance[]; total: number };

// ------------------------------------------------------------------ reconciliation

export type ReconcileCandidate = { glName: string; postingDate: string; voucherType: string | null; voucherNo: string | null; party: string | null; remarks: string | null; debit: number; credit: number };
export type ReconcileCandidates = { account: string; asOf: string; debitNormal: boolean; bookBalance: number; reconciledBalance: number; rows: ReconcileCandidate[]; truncated: boolean };
export type CompleteReconciliationBody = { account: string; statementDate: string; statementBalance: number; glEntryNames: string[]; notes?: string | null };
export type Reconciliation = { id: string; account: string; statementDate: string; statementBalance: number; clearedTotal: number; itemCount: number; completedAt: string; completedBy?: string | null };
export type ReconciliationDetail = { reconciliation: Reconciliation; items: Array<{ glName: string; postingDate: string; voucherType: string | null; voucherNo: string | null; signedAmount: number }> };
export type ReconciliationStatement = {
  account: string; asOf: string; debitNormal: boolean; bookBalance: number; unclearedDebits: number; unclearedCredits: number; statementBalance: number;
  uncleared: ReconcileCandidate[]; lastReconciledOn: string | null;
};

const enc = encodeURIComponent;

export const accountingApi = {
  accounts: (balances: boolean) => apiClient.get('/accounting/accounts', { params: { balances }, timeout: 120000 }),
  mapping: () => apiClient.get('/accounting/accounts/mapping'),
  accountTypes: () => apiClient.get('/accounting/accounts/types'),
  createAccount: (body: CreateAccountBody) => apiClient.post('/accounting/accounts', body),
  updateAccount: (name: string, body: UpdateAccountBody) => apiClient.put(`/accounting/accounts?name=${enc(name)}`, body),
  accountImportTemplate: (format: 'xlsx' | 'csv') => apiClient.get('/accounting/accounts/import/template', { params: { format }, responseType: 'blob' }),
  importAccounts: (file: File, dryRun: boolean) => {
    const body = new FormData();
    body.append('file', file);
    return apiClient.post('/accounting/accounts/import', body, { params: { dryRun }, headers: { 'Content-Type': 'multipart/form-data' }, timeout: 120000 });
  },

  entryTypes: (includeInactive = false) => apiClient.get('/accounting/entry-types', { params: { includeInactive } }),
  createEntryType: (body: SaveEntryTypeBody) => apiClient.post('/accounting/entry-types', body),
  updateEntryType: (id: string, body: SaveEntryTypeBody) => apiClient.put(`/accounting/entry-types/${id}`, body),

  entries: (p: EntryListParams) => apiClient.get('/accounting/entries', { params: p }),
  entry: (id: string) => apiClient.get(`/accounting/entries/${id}`),
  createEntry: (body: SaveEntryBody) => apiClient.post('/accounting/entries', body),
  updateEntry: (id: string, body: SaveEntryBody) => apiClient.put(`/accounting/entries/${id}`, body),
  postEntry: (id: string) => apiClient.post(`/accounting/entries/${id}/post`),
  voidEntry: (id: string, reason: string) => apiClient.post(`/accounting/entries/${id}/void`, { reason }),
  deleteEntry: (id: string) => apiClient.delete(`/accounting/entries/${id}`),

  tags: () => apiClient.get('/accounting/tags'),
  saveTag: (id: string | null, body: { name: string; color?: string }) => (id ? apiClient.put(`/accounting/tags/${id}`, body) : apiClient.post('/accounting/tags', body)),
  deleteTag: (id: string) => apiClient.delete(`/accounting/tags/${id}`),
  attachTag: (id: string, target: TagTarget) => apiClient.post(`/accounting/tags/${id}/attach`, target),
  detachTag: (id: string, target: TagTarget) => apiClient.post(`/accounting/tags/${id}/detach`, target),

  trialBalance: (from: string, to: string, includeZero: boolean) => apiClient.get('/accounting/reports/trial-balance', { params: { from, to, includeZero }, timeout: 120000 }),
  balanceSheet: (asOf: string) => apiClient.get('/accounting/reports/balance-sheet', { params: { asOf }, timeout: 120000 }),
  profitLoss: (from: string, to: string) => apiClient.get('/accounting/reports/profit-loss', { params: { from, to }, timeout: 120000 }),
  ledger: (p: LedgerParams) => apiClient.get('/accounting/reports/ledger', { params: p, timeout: 120000 }),
  partyBalances: (partyType: 'CUSTOMER' | 'VENDOR', asOf: string) => apiClient.get('/accounting/reports/party-balances', { params: { partyType, asOf }, timeout: 120000 }),

  reconcileCandidates: (account: string, asOf: string) => apiClient.get('/accounting/reconciliation/candidates', { params: { account, asOf }, timeout: 120000 }),
  reconciliations: (account?: string) => apiClient.get('/accounting/reconciliation', { params: { account } }),
  reconciliation: (id: string) => apiClient.get(`/accounting/reconciliation/${id}`),
  reconciliationStatement: (account: string, asOf: string) => apiClient.get('/accounting/reconciliation/statement', { params: { account, asOf }, timeout: 120000 }),
  completeReconciliation: (body: CompleteReconciliationBody) => apiClient.post('/accounting/reconciliation', body),
  undoReconciliation: (id: string) => apiClient.delete(`/accounting/reconciliation/${id}`),
};

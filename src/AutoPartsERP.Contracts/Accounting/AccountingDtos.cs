namespace AutoPartsERP.Contracts.Accounting;

// ------------------------------------------------------------------ chart of accounts

public sealed record AccountNodeDto(
    string Name, string AccountName, string? ParentAccount, bool IsGroup, string? RootType, string? AccountType, string? Currency, decimal? Balance);

public sealed record CreateAccountRequest(string AccountName, string ParentAccount, bool IsGroup, string? AccountType, string? AccountNumber);

public sealed record UpdateAccountRequest(string? AccountName, string? AccountType, bool? Disabled);

/// <summary>One line of an account import file. <c>IsGroup</c> null means "read it from the type column"; <c>Error</c> is a problem found while reading the file.</summary>
public sealed record ImportAccountRow(int RowNumber, string? AccountName, string? ParentAccount, bool? IsGroup, string? AccountType, string? AccountNumber, string? Error);

public sealed record ImportAccountRowResult(int RowNumber, string AccountName, string Status, string? Message);

public sealed record ImportAccountsResult(bool DryRun, int Total, int Created, int Existing, int Failed, IReadOnlyList<ImportAccountRowResult> Rows);

// ------------------------------------------------------------------ entry types, entries, tags

public sealed record EntryTypeDto(Guid Id, string Code, string NameAr, string Kind, string Prefix, string? Description, bool IsSystem, bool IsActive);

public sealed record SaveEntryTypeRequest(string NameAr, string Kind, string Prefix, string? Description, bool IsActive);

public sealed record TagDto(Guid Id, string Name, string Color);

public sealed record SaveTagRequest(string Name, string? Color);

/// <summary>Puts a tag on (or takes it off) a manual entry or an ERPNext voucher. Key: the entry id, or "&lt;voucher type&gt;|&lt;voucher number&gt;".</summary>
public sealed record TagTargetRequest(string TargetType, string TargetKey);

public sealed record JournalLineInput(string Account, Guid? PartyId, decimal Debit, decimal Credit, string? Narration);

public sealed record SaveJournalEntryRequest(Guid EntryTypeId, DateOnly EntryDate, string? Narration, string? ReferenceNumber, IReadOnlyList<JournalLineInput> Lines);

public sealed record VoidJournalEntryRequest(string Reason);

public sealed record JournalEntryListDto(
    Guid Id, string EntryNumber, string TypeCode, string TypeNameAr, string Kind, DateOnly EntryDate, string Status, string? Narration, decimal TotalUsd,
    string? ErpNextName, string SyncStatus, string? SyncError, IReadOnlyList<TagDto> Tags);

public sealed record JournalLineDto(int LineNumber, string Account, Guid? PartyId, string? PartyName, decimal Debit, decimal Credit, string? Narration);

public sealed record JournalEntryDetailDto(JournalEntryListDto Entry, string? ReferenceNumber, string? VoidReason, IReadOnlyList<JournalLineDto> Lines);

// ------------------------------------------------------------------ reports

/// <summary>One account in the trial balance. Opening and closing are signed (debit minus credit), so a positive number is a debit balance.</summary>
public sealed record TrialBalanceLineDto(
    string Account, string AccountName, string? Parent, int Depth, bool IsGroup, string? RootType, decimal Opening, decimal Debit, decimal Credit, decimal Closing);

public sealed record TrialBalanceDto(DateOnly From, DateOnly To, IReadOnlyList<TrialBalanceLineDto> Lines, decimal TotalDebit, decimal TotalCredit, bool IsBalanced);

/// <summary>One account in a financial statement, amount in the account's natural direction (assets and expenses debit-positive; the rest credit-positive).</summary>
public sealed record StatementLineDto(string Account, string AccountName, string? Parent, int Depth, bool IsGroup, decimal Amount);

public sealed record BalanceSheetDto(
    DateOnly AsOf, IReadOnlyList<StatementLineDto> Assets, IReadOnlyList<StatementLineDto> Liabilities, IReadOnlyList<StatementLineDto> Equity,
    decimal NetProfit, decimal TotalAssets, decimal TotalLiabilities, decimal TotalEquity, bool IsBalanced);

public sealed record ProfitLossDto(
    DateOnly From, DateOnly To, IReadOnlyList<StatementLineDto> Income, IReadOnlyList<StatementLineDto> Expenses, decimal TotalIncome, decimal TotalExpenses, decimal NetProfit);

public sealed record LedgerRowDto(
    string? GlName, DateOnly PostingDate, string? VoucherType, string? VoucherNo, string? Party, string? Remarks, decimal Debit, decimal Credit, decimal Balance,
    string? LocalEntityType, Guid? LocalEntityId, IReadOnlyList<TagDto> Tags);

/// <summary>One page of a ledger statement. Opening, totals and closing cover the whole period; <c>TotalCount</c> is the number of lines in it.
/// With <c>TagFiltered</c> the statement lists only the tagged vouchers and its balance is their own running total (opening 0).</summary>
public sealed record LedgerStatementDto(
    string Account, string? RootType, DateOnly From, DateOnly To, decimal Opening, IReadOnlyList<LedgerRowDto> Rows,
    decimal TotalDebit, decimal TotalCredit, decimal Closing, long TotalCount, int PageNumber, int PageSize, bool TagFiltered);

public sealed record PartyBalanceDto(
    string Party, Guid? PartyId, Guid? CustomerId, decimal Balance, decimal Current, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Over90, decimal Unallocated);

public sealed record PartyBalancesDto(string PartyType, DateOnly AsOf, IReadOnlyList<PartyBalanceDto> Rows, decimal Total);

// ------------------------------------------------------------------ reconciliation

public sealed record ReconcileCandidateDto(string GlName, DateOnly PostingDate, string? VoucherType, string? VoucherNo, string? Party, string? Remarks, decimal Debit, decimal Credit);

/// <summary>What is left to reconcile on an account up to a date. <c>ReconciledBalance</c> is what earlier reconciliations already cleared, in the account's natural direction.</summary>
public sealed record ReconcileCandidatesDto(
    string Account, DateOnly AsOf, bool DebitNormal, decimal BookBalance, decimal ReconciledBalance, IReadOnlyList<ReconcileCandidateDto> Rows, bool Truncated);

public sealed record CompleteReconciliationRequest(string Account, DateOnly StatementDate, decimal StatementBalance, IReadOnlyList<string> GlEntryNames, string? Notes);

public sealed record ReconciliationListDto(
    Guid Id, string Account, DateOnly StatementDate, decimal StatementBalance, decimal ClearedTotal, int ItemCount, DateTimeOffset CompletedAt, string? CompletedBy);

public sealed record ReconciliationItemDto(string GlName, DateOnly PostingDate, string? VoucherType, string? VoucherNo, decimal SignedAmount);

public sealed record ReconciliationDetailDto(ReconciliationListDto Reconciliation, IReadOnlyList<ReconciliationItemDto> Items);

/// <summary>The classic reconciliation statement: balance per books, less/plus what has not cleared, equals the balance per statement.</summary>
public sealed record ReconciliationStatementDto(
    string Account, DateOnly AsOf, bool DebitNormal, decimal BookBalance, decimal UnclearedDebits, decimal UnclearedCredits, decimal StatementBalance,
    IReadOnlyList<ReconcileCandidateDto> Uncleared, DateOnly? LastReconciledOn);

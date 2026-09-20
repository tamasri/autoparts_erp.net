namespace AutoPartsERP.Application.Common.Abstractions;

/// <summary>
/// Boundary for the accounting engine hand-off decided for AutoPartsERP: this app stays the
/// system of record for items, inventory, warehouse operations, warranty and governance; ERPNext
/// (headless - no ERPNext UI is exposed to end users) becomes the system of record for the chart
/// of accounts, general ledger, payments, purchase invoices and financial reports. This interface
/// is the only door between the two.
///
/// No implementation calls a live ERPNext instance yet - none is deployed. <see cref="NullErpNextClient"/>
/// is registered until <c>ErpNext:Enabled</c> is turned on in configuration, so every call site
/// wired to this interface today (starting with InvoicePostedOutboxHandler) is inert but ready:
/// flipping the config on later requires no code changes at the call sites.
/// </summary>
public interface IErpNextClient
{
    bool IsEnabled { get; }

    Task<Result<string>> SyncItemAsync(ErpNextItemSync item, CancellationToken cancellationToken = default);

    Task<Result<string>> SyncPartyAsync(ErpNextPartySync party, CancellationToken cancellationToken = default);

    Task<Result<string>> SyncSalesInvoiceAsync(ErpNextSalesInvoiceSync invoice, CancellationToken cancellationToken = default);

    Task<Result<string>> SyncPaymentAsync(ErpNextPaymentSync payment, CancellationToken cancellationToken = default);

    /// <summary>Books cost of goods sold (Dr COGS / Cr Inventory) for a posted invoice; reversed for customer returns.</summary>
    Task<Result<string>> SyncCogsEntryAsync(ErpNextCogsEntrySync entry, CancellationToken cancellationToken = default);

    Task<Result<string>> SyncPurchaseInvoiceAsync(ErpNextPurchaseInvoiceSync bill, CancellationToken cancellationToken = default);

    Task<Result<string>> SyncSupplierPaymentAsync(ErpNextSupplierPaymentSync payment, CancellationToken cancellationToken = default);

    /// <summary>Renames a document (ERPNext identifies customers/suppliers by name); links to it follow automatically.</summary>
    Task<Result<string>> RenameDocumentAsync(string doctype, string oldName, string newName, CancellationToken cancellationToken = default);

    /// <summary>Cancels a submitted ERPNext document (Sales Invoice, Payment Entry, ...) by its ERPNext name.</summary>
    Task<Result<string>> CancelDocumentAsync(string doctype, string name, CancellationToken cancellationToken = default);

    /// <summary>The company's chart of accounts as ERPNext holds it, parents before children. Balances come from <see cref="GetGlBalancesAsync"/>.</summary>
    Task<Result<IReadOnlyList<ErpNextAccount>>> GetChartOfAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>Which ERPNext account each application event posts to (receivable, cash, COGS, inventory, ...).</summary>
    Task<Result<IReadOnlyList<ErpNextAccountMapping>>> GetAccountMappingAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a group or ledger account under an existing parent; returns the account's ERPNext name.</summary>
    Task<Result<string>> CreateAccountAsync(ErpNextAccountCreate account, CancellationToken cancellationToken = default);

    /// <summary>Renames an account, changes its type or disables it; returns its (possibly new) ERPNext name.</summary>
    Task<Result<string>> UpdateAccountAsync(string name, ErpNextAccountUpdate update, CancellationToken cancellationToken = default);

    /// <summary>Books a manual accounting entry as a submitted Journal Entry.</summary>
    Task<Result<string>> SyncJournalEntryAsync(ErpNextJournalEntrySync entry, CancellationToken cancellationToken = default);

    /// <summary>Debit and credit totals per account from the general ledger, for postings between the two dates (either may be open).</summary>
    Task<Result<IReadOnlyList<ErpNextGlBalance>>> GetGlBalancesAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);

    /// <summary>Ledger lines in date order. At most <c>Limit</c> rows are returned; one extra tells the caller the result was cut.</summary>
    Task<Result<IReadOnlyList<ErpNextGlEntry>>> GetGlEntriesAsync(ErpNextGlFilter filter, CancellationToken cancellationToken = default);

    /// <summary>What each customer or supplier owes or is owed according to the ledger, up to a date.</summary>
    Task<Result<IReadOnlyList<ErpNextPartyBalance>>> GetPartyBalancesAsync(string partyType, DateOnly asOf, CancellationToken cancellationToken = default);

    /// <summary>Submitted sales or purchase invoices that still have an outstanding amount, for ageing.</summary>
    Task<Result<IReadOnlyList<ErpNextOpenInvoice>>> GetOpenInvoicesAsync(string doctype, DateOnly asOf, CancellationToken cancellationToken = default);
}

public sealed record ErpNextAccount(string Name, string AccountName, string? ParentAccount, bool IsGroup, string? RootType, string? AccountType, string? Currency);

public sealed record ErpNextAccountMapping(string Purpose, string Description, string? Account);

public sealed record ErpNextAccountCreate(string AccountName, string ParentAccount, bool IsGroup, string? AccountType, string? AccountNumber);

public sealed record ErpNextAccountUpdate(string? AccountName, string? AccountType, bool? Disabled);

public sealed record ErpNextJournalLine(string Account, string? PartyType, string? Party, decimal Debit, decimal Credit, string? Remark);

/// <summary><c>VoucherType</c> is ERPNext's ("Journal Entry", "Bank Entry", "Contra Entry", ...); <c>EntryNumber</c> is our own number, kept as the entry's reference.</summary>
public sealed record ErpNextJournalEntrySync(
    Guid LocalId, string VoucherType, string EntryNumber, DateOnly Date, string? Narration, string? ReferenceNo, bool IsOpening, IReadOnlyList<ErpNextJournalLine> Lines);

public sealed record ErpNextGlBalance(string Account, decimal Debit, decimal Credit);

public sealed record ErpNextGlEntry(
    string Name, DateOnly PostingDate, string Account, string? PartyType, string? Party, decimal Debit, decimal Credit, string? VoucherType, string? VoucherNo, string? Remarks);

public sealed record ErpNextGlFilter(string? Account, string? PartyType, string? Party, DateOnly? From, DateOnly? To, int Limit);

public sealed record ErpNextPartyBalance(string Party, decimal Debit, decimal Credit);

public sealed record ErpNextOpenInvoice(string Name, string Party, DateOnly PostingDate, DateOnly? DueDate, decimal Outstanding);

public sealed record ErpNextItemSync(Guid LocalItemId, string Code, string NameEn, string NameAr, decimal CostPrice, decimal SellingPrice);

public sealed record ErpNextPartySync(Guid LocalPartyId, string Name, string PartyType, string? TaxId);

public sealed record ErpNextSalesInvoiceSync(
    Guid LocalInvoiceId,
    string InvoiceNumber,
    string CustomerName,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    IReadOnlyList<ErpNextInvoiceLineSync> Lines,
    bool IsReturn = false,
    string? ReturnAgainst = null);

/// <summary>A cost-of-goods Journal Entry (Dr COGS / Cr Inventory, reversed when <c>IsReturn</c>). <c>Description</c> replaces the default invoice remark, e.g. for an inventory adjustment.</summary>
public sealed record ErpNextCogsEntrySync(Guid LocalInvoiceId, string InvoiceNumber, DateOnly Date, decimal Amount, bool IsReturn, string? Description = null);

public sealed record ErpNextPurchaseInvoiceSync(
    Guid LocalId,
    string BillNumber,
    string SupplierName,
    DateOnly BillDate,
    DateOnly DueDate,
    bool IsReturn,
    IReadOnlyList<ErpNextInvoiceLineSync> Lines);

public sealed record ErpNextSupplierPaymentSync(
    Guid LocalPaymentId,
    string SupplierName,
    decimal Amount,
    DateOnly PaymentDate,
    string PaymentMethod,
    string? ReferenceNumber,
    IReadOnlyList<ErpNextPaymentReference> References);

public sealed record ErpNextInvoiceLineSync(string ItemCode, decimal Quantity, decimal UnitPrice, decimal DiscountPercent);

/// <summary>A customer receipt. Amount is in the ERPNext company currency (USD); References are the Sales Invoices it settles.</summary>
public sealed record ErpNextPaymentSync(
    Guid LocalPaymentId,
    string CustomerName,
    decimal Amount,
    DateOnly PaymentDate,
    string PaymentMethod,
    string? ReferenceNumber,
    IReadOnlyList<ErpNextPaymentReference> References);

public sealed record ErpNextPaymentReference(string DocumentName, decimal AllocatedAmount);

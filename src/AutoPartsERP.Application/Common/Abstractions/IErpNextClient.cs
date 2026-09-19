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
}

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

public sealed record ErpNextCogsEntrySync(Guid LocalInvoiceId, string InvoiceNumber, DateOnly Date, decimal Amount, bool IsReturn);

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

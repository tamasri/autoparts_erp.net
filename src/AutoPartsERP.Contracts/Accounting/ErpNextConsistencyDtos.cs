namespace AutoPartsERP.Contracts.Accounting;

/// <summary>
/// Local records against ERPNext, section by section. Nothing is fixed by the check: every difference is listed so a person decides.
/// Totals are in dollars (the company currency); a section without amounts (customers, suppliers, items) leaves them null.
/// </summary>
public sealed record ErpNextConsistencyDto(DateTimeOffset CheckedAt, IReadOnlyList<ErpNextConsistencySectionDto> Sections);

/// <param name="Key">customers, suppliers, items, sales-invoices, purchase-invoices, payments, journal-entries.</param>
/// <param name="LocalCount">Local records that should be in ERPNext (active / posted).</param>
/// <param name="ErpNextCount">Active documents in ERPNext (submitted, or not disabled for master data).</param>
/// <param name="IssueCounts">Number of issues per kind; <c>Issues</c> lists at most 200 of them.</param>
/// <param name="Error">Set when ERPNext could not be read for this section; the local side is still shown.</param>
public sealed record ErpNextConsistencySectionDto(
    string Key,
    string Doctype,
    int LocalCount,
    decimal? LocalTotal,
    int ErpNextCount,
    decimal? ErpNextTotal,
    int MatchedCount,
    IReadOnlyDictionary<string, int> IssueCounts,
    IReadOnlyList<ErpNextConsistencyIssueDto> Issues,
    string? Error);

/// <param name="Kind">
/// NOT_SENT (never reached ERPNext; <c>Detail</c> has the last error), MISSING_IN_ERPNEXT (sent, but ERPNext no longer has it),
/// ONLY_IN_ERPNEXT, CANCELLED_IN_ERPNEXT_ONLY, NOT_CANCELLED_IN_ERPNEXT, AMOUNT_DIFFERS.
/// </param>
/// <param name="LocalEntityType">The sync-log entity (Invoice, PurchaseInvoice, Payment, SupplierPayment, JournalEntry, StockAdjustment, Party, Sku) — the screen links it to the local record.</param>
public sealed record ErpNextConsistencyIssueDto(
    string Kind,
    string? LocalEntityType,
    Guid? LocalId,
    string? LocalRef,
    string? ErpNextName,
    decimal? LocalAmount,
    decimal? ErpNextAmount,
    string? Detail);

/// <summary>One read-only list from ERPNext (cost centres, modes of payment, tax templates, fiscal years, exchange rates). Values are as ERPNext returns them.</summary>
public sealed record ErpNextReferenceDto(string Kind, string Doctype, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);

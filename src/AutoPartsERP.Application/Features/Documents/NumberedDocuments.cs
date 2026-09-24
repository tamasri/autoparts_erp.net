namespace AutoPartsERP.Application.Features.Documents;

/// <summary>
/// The numbered document tables, by the kind name the API uses. Table and column names in SQL come only from here, never from input.
/// The database numbers every one of them (migration AddDocumentNumbering); a kind with <see cref="Deletion"/> can be deleted by the
/// Super Admin while it is a draft or voided.
/// </summary>
public sealed record NumberedDocumentKind(string Kind, string Table, string NumberColumn, string ReadPermission, DocumentDeletion? Deletion = null);

/// <summary>How a deletable kind is deleted: its lines table and link column, and the date and module its period lock is checked by.</summary>
public sealed record DocumentDeletion(string LinesTable, string LinesForeignKey, string DateColumn, string PeriodModule);

public static class NumberedDocuments
{
    public const string Invoices = "invoices";
    public const string PurchaseInvoices = "purchase-invoices";
    public const string JournalEntries = "journal-entries";

    public static readonly IReadOnlyList<NumberedDocumentKind> All =
    [
        new(Invoices, "invoices", "invoice_number", PermissionCodes.Invoices.Read,
            new DocumentDeletion("invoice_lines", "invoice_id", "invoice_date", "INVOICES")),
        new("payments", "payments", "payment_number", PermissionCodes.Payments.Read),
        new(PurchaseInvoices, "purchase_invoices", "bill_number", PermissionCodes.Purchases.Read,
            new DocumentDeletion("purchase_invoice_lines", "purchase_invoice_id", "bill_date", "PURCHASES")),
        new("supplier-payments", "supplier_payments", "payment_number", PermissionCodes.SupplierPayments.Read),
        new(JournalEntries, "journal_entries", "entry_number", PermissionCodes.Accounting.Read,
            new DocumentDeletion("journal_entry_lines", "journal_entry_id", "entry_date", "ACCOUNTING")),
        new("stock-adjustments", "stock_adjustments", "adjustment_no", PermissionCodes.StockAdjustments.Read),
        new("transfer-orders", "transfer_orders", "transfer_no", PermissionCodes.Transfers.Read),
        new("receiving", "receiving_documents", "document_no", PermissionCodes.Receiving.Read),
        new("issue-orders", "issue_orders", "order_no", PermissionCodes.IssueOrders.Read),
    ];

    private static readonly Dictionary<string, NumberedDocumentKind> ByKind = All.ToDictionary(k => k.Kind, StringComparer.Ordinal);

    public static NumberedDocumentKind? Find(string? kind) => kind is not null && ByKind.TryGetValue(kind, out var found) ? found : null;

    /// <summary>A permission nobody holds: requests for an unknown kind are refused by the validator before this is ever checked.</summary>
    internal const string NoSuchKind = "documents:no_such_kind";
}

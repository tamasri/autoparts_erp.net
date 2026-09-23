namespace AutoPartsERP.Application.Features.Assistant;

/// <summary>One argument of a tool the language model may choose.</summary>
public sealed record ToolParameter(string Name, string Description, bool Required, IReadOnlyList<string>? Allowed = null);

/// <summary>A question the assistant can answer. The model only picks the tool and copies the words; it never sees any data.</summary>
public sealed record ToolSpec(string Name, string Description, IReadOnlyList<ToolParameter> Parameters);

/// <summary>
/// Everything the WhatsApp assistant can do. All read-only: nothing here creates, changes or posts a document, so a misunderstood
/// message can at worst produce a wrong answer to a question, never a wrong invoice.
/// </summary>
public static class AssistantActions
{
    public const string CustomerBalance = "customer_balance";
    public const string ItemStock = "item_stock";
    public const string InvoiceStatus = "invoice_status";
    public const string SalesSummary = "sales_summary";
    public const string OverdueInvoices = "overdue_invoices";
    public const string Help = "help";

    public static readonly IReadOnlyList<string> Periods = ["today", "yesterday", "this_week", "this_month", "last_month"];

    public static readonly IReadOnlyList<ToolSpec> Tools =
    [
        new(CustomerBalance, "Outstanding balance (what the customer still owes) of one customer.",
            [new("customer_name", "Customer name exactly as the user wrote it (may contain spelling mistakes; do not correct it).", true)]),
        new(ItemStock, "Available stock of a spare part / item, per warehouse.",
            [new("item", "Item name, part number or code exactly as the user wrote it.", true),
             new("warehouse", "Warehouse name if the user named one.", false)]),
        new(InvoiceStatus, "Status, total, paid and remaining amount of one sales invoice.",
            [new("invoice_number", "The invoice number as written (full, like INV-YYYY-NNNNN, or just its digits).", true)]),
        new(SalesSummary, "Sales totals (net sales, gross profit, number of invoices) for a period.",
            [new("period", "The period asked about.", true, Periods)]),
        new(OverdueInvoices, "Invoices past their due date that are still unpaid, optionally for one customer.",
            [new("customer_name", "Customer name if the user asked about one customer.", false)]),
        new(Help, "The user greets, asks what the assistant can do, or asks something none of the other tools answers.", []),
    ];
}

namespace AutoPartsERP.Domain.Constants;

/// <summary>The account types ERPNext accepts on a ledger account (its own fixed list). Group accounts carry no type.</summary>
public static class ErpNextAccountTypes
{
    public static readonly IReadOnlyCollection<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "Accumulated Depreciation", "Asset Received But Not Billed", "Bank", "Cash", "Chargeable", "Capital Work in Progress", "Cost of Goods Sold",
        "Current Asset", "Current Liability", "Depreciation", "Direct Expense", "Direct Income", "Equity", "Expense Account",
        "Expenses Included In Asset Valuation", "Expenses Included In Valuation", "Fixed Asset", "Income Account", "Indirect Expense",
        "Indirect Income", "Liability", "Payable", "Receivable", "Round Off", "Service Received But Not Billed", "Stock",
        "Stock Adjustment", "Stock Received But Not Billed", "Tax", "Temporary"
    };
}

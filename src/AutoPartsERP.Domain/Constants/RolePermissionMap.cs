namespace AutoPartsERP.Domain.Constants;

/// <summary>
/// Canonical mapping between governance <see cref="RoleCodes"/> and the <see cref="PermissionCodes"/>
/// each role is granted. This is the single source of truth consumed by the database seeder
/// (to create roles and attach their permission claims) and by any future role-management UI.
/// </summary>
public static class RolePermissionMap
{
    /// <summary>
    /// Returns the ordered role -&gt; permission-code bundles for all <see cref="RoleCodes.All"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> Bundles =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
        {
            // Full platform owner: every permission that exists.
            [RoleCodes.SystemAdministrator] = PermissionCodes.All.ToArray(),

            // Identity & access management only.
            [RoleCodes.SecurityAdministrator] = new[]
            {
                PermissionCodes.UsersRead,
                PermissionCodes.UsersWrite,
                PermissionCodes.UsersManageRoles,
                PermissionCodes.RolesRead,
                PermissionCodes.RolesWrite,
                PermissionCodes.AuthManage
            },

            // Governance oversight: audit, period locks, reason codes, approvals visibility.
            [RoleCodes.ComplianceOfficer] = new[]
            {
                PermissionCodes.AuditRead,
                PermissionCodes.PeriodLocksRead,
                PermissionCodes.PeriodLocksWrite,
                PermissionCodes.ReasonCodesRead,
                PermissionCodes.ReasonCodesWrite,
                PermissionCodes.ApprovalsRead
            },

            // Maker-checker second signer.
            [RoleCodes.Approver] = new[]
            {
                PermissionCodes.ApprovalsRead,
                PermissionCodes.ApprovalsReview,
                PermissionCodes.ApprovalsWrite
            },

            // Read-only across the platform plus audit.
            [RoleCodes.Auditor] = BuildAuditorBundle(),

            // Ledger and money: bills and supplier payments, customer receipts, FX rates and statements.
            [RoleCodes.Accountant] = new[]
            {
                PermissionCodes.Purchases.Read,
                PermissionCodes.Purchases.Post,
                PermissionCodes.Purchases.Void,
                PermissionCodes.SupplierPayments.Read,
                PermissionCodes.SupplierPayments.Create,
                PermissionCodes.SupplierPayments.Reverse,
                PermissionCodes.Payments.Read,
                PermissionCodes.Payments.Create,
                PermissionCodes.Payments.Allocate,
                PermissionCodes.Invoices.Read,
                PermissionCodes.FxRates.Read,
                PermissionCodes.FxRates.Manage,
                PermissionCodes.Reports.AccountStatement,
                PermissionCodes.Reports.ProfitLoss,
                PermissionCodes.Accounting.Read,
                PermissionCodes.Accounting.ManageAccounts,
                PermissionCodes.Accounting.PostEntries,
                PermissionCodes.Accounting.Reconcile,
                PermissionCodes.Customers.Read,
                PermissionCodes.Party.Read,
                PermissionCodes.Items.Read,
                PermissionCodes.Inventory.Read,
                PermissionCodes.PeriodLocksRead
            },

            // Buys goods: prepares and posts supplier bills, sees stock and suppliers; does not pay or void.
            [RoleCodes.Purchaser] = new[]
            {
                PermissionCodes.Purchases.Read,
                PermissionCodes.Purchases.Create,
                PermissionCodes.Purchases.Post,
                PermissionCodes.SupplierPayments.Read,
                PermissionCodes.Party.Read,
                PermissionCodes.Party.Create,
                PermissionCodes.Items.Read,
                PermissionCodes.Catalog.Read,
                PermissionCodes.Inventory.Read,
                PermissionCodes.Receiving.Read
            },

            // Warehouse staff: receiving, putaway, transfers, picking, counts and adjustments. Which warehouses a person works in, and whether
            // they manage (approve transfers for) them, is set per user by an administrator, not by this role.
            [RoleCodes.Warehouse] = new[]
            {
                PermissionCodes.Items.Read,
                PermissionCodes.Catalog.Read,
                PermissionCodes.Inventory.Read,
                PermissionCodes.Inventory.Transfer,
                PermissionCodes.Inventory.ViewBatches,
                PermissionCodes.Receiving.Read,
                PermissionCodes.Receiving.Create,
                PermissionCodes.Receiving.Post,
                PermissionCodes.Receiving.Putaway,
                PermissionCodes.Transfers.Read,
                PermissionCodes.Transfers.CreateRequest,
                PermissionCodes.Transfers.CreateOrder,
                PermissionCodes.Transfers.Ship,
                PermissionCodes.Transfers.Receive,
                PermissionCodes.IssueOrders.Read,
                PermissionCodes.IssueOrders.Pick,
                PermissionCodes.IssueOrders.Verify,
                PermissionCodes.IssueOrders.Issue,
                PermissionCodes.CycleCounts.Read,
                PermissionCodes.CycleCounts.Record,
                PermissionCodes.StockAdjustments.Read,
                PermissionCodes.StockAdjustments.Create,
                PermissionCodes.InventoryAlerts.Read,
                PermissionCodes.InventoryAlerts.Acknowledge,
                PermissionCodes.Barcodes.Scan
            },

            // Baseline authenticated user: assorted read access.
            [RoleCodes.StandardUser] = new[]
            {
                PermissionCodes.Customers.Read,
                PermissionCodes.Catalog.Read,
                PermissionCodes.Items.Read,
                PermissionCodes.Inventory.Read,
                PermissionCodes.Invoices.Read,
                PermissionCodes.Party.Read
            }
        };

    private static string[] BuildAuditorBundle()
    {
        var reads = PermissionCodes.All
            .Where(code => code.EndsWith(":read", StringComparison.Ordinal)
                || code.EndsWith(".read", StringComparison.Ordinal))
            .ToList();

        reads.Add(PermissionCodes.AuditRead);

        return reads
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
    }
}

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

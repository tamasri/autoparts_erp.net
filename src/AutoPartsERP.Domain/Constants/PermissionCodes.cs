namespace AutoPartsERP.Domain.Constants;

public static class PermissionCodes
{
    public const string UsersRead = "users.read";
    public const string UsersWrite = "users.write";
    public const string UsersManageRoles = "users.manage-roles";
    public const string RolesRead = "roles.read";
    public const string RolesWrite = "roles.write";
    public const string ApprovalsRead = "approvals.read";
    public const string ApprovalsWrite = "approvals.write";
    public const string ApprovalsReview = "approvals.review";
    public const string PeriodLocksRead = "period-locks.read";
    public const string PeriodLocksWrite = "period-locks.write";
    public const string ReasonCodesRead = "reason-codes.read";
    public const string ReasonCodesWrite = "reason-codes.write";
    public const string AuditRead = "audit.read";
    public const string AuthManage = "auth.manage";

    public static readonly IReadOnlyCollection<string> All = new[]
    {
        UsersRead,
        UsersWrite,
        UsersManageRoles,
        RolesRead,
        RolesWrite,
        ApprovalsRead,
        ApprovalsWrite,
        ApprovalsReview,
        PeriodLocksRead,
        PeriodLocksWrite,
        ReasonCodesRead,
        ReasonCodesWrite,
        AuditRead,
        AuthManage
    };
}

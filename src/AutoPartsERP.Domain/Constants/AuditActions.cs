namespace AutoPartsERP.Domain.Constants;

public static class AuditActions
{
    public const string Login = "AUTH.LOGIN";
    public const string Logout = "AUTH.LOGOUT";
    public const string TokenRefreshed = "AUTH.TOKEN_REFRESHED";
    public const string UserCreated = "USER.CREATED";
    public const string UserUpdated = "USER.UPDATED";
    public const string UserActivated = "USER.ACTIVATED";
    public const string UserDeactivated = "USER.DEACTIVATED";
    public const string RoleCreated = "ROLE.CREATED";
    public const string RoleUpdated = "ROLE.UPDATED";
    public const string RoleAssigned = "ROLE.ASSIGNED";
    public const string ApprovalRequested = "APPROVAL.REQUESTED";
    public const string ApprovalApproved = "APPROVAL.APPROVED";
    public const string ApprovalRejected = "APPROVAL.REJECTED";
    public const string ApprovalCancelled = "APPROVAL.CANCELLED";
    public const string PeriodLocked = "PERIOD.LOCKED";
    public const string PeriodUnlocked = "PERIOD.UNLOCKED";
    public const string ReasonCodeCreated = "REASON_CODE.CREATED";
    public const string ReasonCodeUpdated = "REASON_CODE.UPDATED";
	public const string PeriodLockBlocked = "PERIOD_LOCK_BLOCKED";
	public const string IdempotencyReplay  = "IDEMPOTENCY_REPLAY";
}

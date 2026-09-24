namespace AutoPartsERP.Application.Common.Abstractions;

/// <summary>Events pushed to signed-in browsers. The names are the contract with <c>frontend/src/hooks/useSignalR.ts</c>.</summary>
public static class RealtimeEvents
{
    /// <summary>A request waits for approval (to reviewers, and to the managers of the warehouses a transfer touches).</summary>
    public const string NewApprovalRequest = "NewApprovalRequest";

    /// <summary>The requester's request was approved or rejected.</summary>
    public const string ApprovalDecided = "ApprovalDecided";

    /// <summary>Items fell to or below their reorder level, or ran out (to users who can see stock alerts).</summary>
    public const string StockAlert = "StockAlert";
}

/// <summary>
/// Who receives an event is decided on the server only: a connection is put, when it opens, into its user's group and into one
/// group per notification permission the user holds (<see cref="NotifiedPermissions"/>). Clients cannot choose groups.
/// </summary>
public static class RealtimeGroups
{
    public static readonly IReadOnlyList<string> NotifiedPermissions =
    [
        PermissionCodes.ApprovalsReview,
        PermissionCodes.InventoryAlerts.Read,
    ];

    public static string User(Guid userId) => $"user:{userId:N}";

    public static string Permission(string code) => $"perm:{code}";
}

/// <summary>
/// Best-effort push to browsers. Never throws and never fails the business operation that triggered it: a missed notification is
/// only a missed toast, the data itself is always on its screen.
/// </summary>
public interface IRealtimeNotifier
{
    Task ToPermissionAsync(string permission, string eventName, object payload, CancellationToken cancellationToken = default);

    Task ToUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload, CancellationToken cancellationToken = default);
}

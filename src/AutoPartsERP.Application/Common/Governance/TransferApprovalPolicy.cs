namespace AutoPartsERP.Application.Common.Governance;

/// <summary>
/// Who may approve a transfer between warehouses, and when it is approved. Pure rules, no data access:
/// <list type="bullet">
/// <item>Every warehouse the transfer touches must consent. A warehouse consents through one of its managers (assigned by an administrator);
/// a manager may approve a transfer into or out of any warehouse they manage, and one approval covers every such warehouse at once.</item>
/// <item>The requester's own managed warehouses count as consenting (a manager moving their own stock agrees to it).</item>
/// <item>SYSTEM_ADMIN approves everything in one step.</item>
/// <item>Managers approve transfers only; other governed requests keep their own approvers.</item>
/// </list>
/// </summary>
public static class TransferApprovalPolicy
{
    public sealed record Outcome(bool Allowed, bool Completes, Error? Error);

    public static readonly Error NotAManager = new("Authorization.NotWarehouseManager", "Only a manager of one of the warehouses in this transfer (or a system administrator) can review it.");

    public static readonly Error NothingToAdd = new("Approval.AlreadyCovered", "Your warehouses have already approved this transfer; a manager of the other warehouse must approve it.");

    /// <summary>The warehouses of the transfer that still need a manager's approval, once the requester's own warehouses are counted.</summary>
    public static IReadOnlyList<Guid> Pending(IReadOnlyCollection<Guid> scope, IReadOnlySet<Guid> requesterManaged) =>
        scope.Distinct().Where(w => !requesterManaged.Contains(w)).ToList();

    public static Outcome EvaluateApproval(
        IReadOnlyCollection<Guid> scope,
        IReadOnlySet<Guid> requesterManaged,
        IEnumerable<IReadOnlySet<Guid>> earlierApprovers,
        IReadOnlySet<Guid> reviewerManaged,
        bool reviewerIsSystemAdministrator)
    {
        if (reviewerIsSystemAdministrator)
        {
            return new Outcome(true, true, null);
        }

        var warehouses = scope.Distinct().ToList();
        var mine = warehouses.Where(reviewerManaged.Contains).ToHashSet();
        if (mine.Count == 0)
        {
            return new Outcome(false, false, NotAManager);
        }

        var covered = warehouses.Where(w => requesterManaged.Contains(w) || earlierApprovers.Any(a => a.Contains(w))).ToHashSet();
        if (mine.IsSubsetOf(covered))
        {
            return new Outcome(false, false, NothingToAdd);
        }

        covered.UnionWith(mine);
        return new Outcome(true, covered.Count == warehouses.Count, null);
    }

    public static bool CanReject(IReadOnlyCollection<Guid> scope, IReadOnlySet<Guid> reviewerManaged, bool reviewerIsSystemAdministrator) =>
        reviewerIsSystemAdministrator || scope.Any(reviewerManaged.Contains);
}

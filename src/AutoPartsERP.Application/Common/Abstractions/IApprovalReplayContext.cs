namespace AutoPartsERP.Application.Common.Abstractions;

/// <summary>
/// Scoped flag that lets <see cref="GovernanceService"/> (Infrastructure) replay an approved
/// command back through MediatR without <c>MakerCheckerBehavior</c> re-intercepting it and
/// creating a second pending approval. Must be registered as Scoped so it is shared by every
/// service resolved within the same HTTP request/job execution.
/// </summary>
public interface IApprovalReplayContext
{
    bool IsReplaying { get; set; }
}

public sealed class ApprovalReplayContext : IApprovalReplayContext
{
    public bool IsReplaying { get; set; }
}

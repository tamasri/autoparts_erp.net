using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Application.Common.Abstractions.Markers;
using AutoPartsERP.Application.Common.Models;

namespace AutoPartsERP.Application.Common.Behaviors;

public sealed class MakerCheckerBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : class
{
    private readonly IApprovalService _approvalService;
    private readonly ICurrentUser _currentUser;
    private readonly IManualAuditService _audit;

    public MakerCheckerBehavior(
        IApprovalService approvalService,
        ICurrentUser currentUser,
        IManualAuditService audit)
    {
        _approvalService = approvalService;
        _currentUser     = currentUser;
        _audit           = audit;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IMakerCheckerRequest makerCheckerRequest
            || !makerCheckerRequest.RequiresApproval)
        {
            return await next();
        }

        var module = request is IAuditableRequest auditableRequest
            ? auditableRequest.AuditModule
            : "APPROVALS";

        var creationResult = await _approvalService.CreatePendingApprovalAsync(
            new PendingApprovalSubmission(
                _currentUser.CorrelationId,
                typeof(TRequest).Name,
                typeof(TRequest).Name.Replace("Command", string.Empty,
                    StringComparison.Ordinal),
                null,
                JsonSerializer.Serialize(request),
                _currentUser.UserId,
                null,
                null,
                module),
            cancellationToken);

        if (creationResult.IsFailure)
        {
            return ResultFactory.Failure<TResponse>(creationResult.Error);
        }

        var approvalId = creationResult.Value;

        await _audit.LogAsync(
            new ManualAuditEntry(
                _currentUser.CorrelationId,
                AuditActions.ApprovalRequested,
                module,
                nameof(ApprovalRequest),
                approvalId,
                _currentUser.UserId,
                _currentUser.Username,
                "CREATE",
                "PENDING",
                IpAddress: _currentUser.IpAddress,
                UserAgent: _currentUser.UserAgent),
            cancellationToken);

        return ResultFactory.Failure<TResponse>(
            new Error("Approval.Pending",
                $"Request submitted for approval. Approval ID: {approvalId}"));
    }
}

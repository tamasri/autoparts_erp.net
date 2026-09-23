using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Application.Common.Abstractions.Markers;
using AutoPartsERP.Application.Common.Models;
using AutoPartsERP.Application.Common.Governance;
using AutoPartsERP.Domain.Constants;

namespace AutoPartsERP.Application.Common.Behaviors;

public sealed class MakerCheckerBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : class
{
    private readonly IApprovalService _approvalService;
    private readonly ICurrentUser _currentUser;
    private readonly IManualAuditService _audit;
    private readonly IApprovalReplayContext _replayContext;
    private readonly IWarehouseAccess _warehouses;

    public MakerCheckerBehavior(
        IApprovalService approvalService,
        ICurrentUser currentUser,
        IManualAuditService audit,
        IApprovalReplayContext replayContext,
        IWarehouseAccess warehouses)
    {
        _approvalService = approvalService;
        _currentUser     = currentUser;
        _audit           = audit;
        _replayContext   = replayContext;
        _warehouses      = warehouses;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // An approved request being replayed by GovernanceService must reach the real handler,
        // not be intercepted again and turned into a second pending approval.
        if (request is not IMakerCheckerRequest makerCheckerRequest
            || !makerCheckerRequest.RequiresApproval
            || _replayContext.IsReplaying)
        {
            return await next();
        }

        // SYSTEM_ADMIN bypasses all approval — they have the full platform picture and are trusted.
        if (_currentUser.HasRole(RoleCodes.SystemAdministrator))
        {
            return await next();
        }

        // A transfer between warehouses is approved by those warehouses' managers. It needs no approval when it stays inside one warehouse, or
        // when the requester manages every warehouse it touches.
        string? entityId = null;
        var requiredApprovals = 1;
        IReadOnlyList<Guid>? scope = null;
        if (request is IWarehouseTransferRequest transfer)
        {
            var resolved = await _warehouses.ResolveTransferAsync(transfer, cancellationToken);
            if (resolved.IsFailure)
            {
                return ResultFactory.Failure<TResponse>(resolved.Error);
            }

            if (resolved.Value is null)
            {
                return await next();
            }

            var pending = TransferApprovalPolicy.Pending(resolved.Value.Warehouses, await _warehouses.ManagedWarehousesAsync(_currentUser.UserId, cancellationToken));
            if (pending.Count == 0)
            {
                return await next();
            }

            if (await _approvalService.HasPendingAsync(typeof(TRequest).Name, resolved.Value.EntityId, cancellationToken))
            {
                return ResultFactory.Failure<TResponse>(new Error("Approval.PendingConflict", "This transfer is already waiting for approval."));
            }

            entityId = resolved.Value.EntityId;
            scope = resolved.Value.Warehouses;
            requiredApprovals = pending.Count;
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
                entityId,
                JsonSerializer.Serialize(request),
                _currentUser.UserId,
                null,
                null,
                module,
                requiredApprovals,
                scope),
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

using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Application.Common.Abstractions.Markers;
using AutoPartsERP.Application.Common.Models;

namespace AutoPartsERP.Application.Common.Behaviors;

public sealed class PeriodLockBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : class
{
    private readonly IPeriodLockService _periodLockService;
    private readonly ICurrentUser _currentUser;
    private readonly IManualAuditService _audit;

    public PeriodLockBehavior(IPeriodLockService periodLockService, ICurrentUser currentUser, IManualAuditService audit)
    {
        _periodLockService = periodLockService;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IPeriodSensitiveRequest periodSensitiveRequest)
        {
            return await next();
        }

        var isLocked = await _periodLockService.IsLockedAsync(periodSensitiveRequest.OperationDate.Year, periodSensitiveRequest.OperationDate.Month, periodSensitiveRequest.Module, cancellationToken);
        if (!isLocked)
        {
            return await next();
        }

        await _audit.LogAsync(
            new ManualAuditEntry(
                _currentUser.CorrelationId,
                AuditActions.PeriodLockBlocked,
                periodSensitiveRequest.Module,
                typeof(TRequest).Name,
                null,
                _currentUser.UserId,
                _currentUser.Username,
                "BLOCKED",
                "REJECTED",
                IpAddress: _currentUser.IpAddress,
                UserAgent: _currentUser.UserAgent),
            cancellationToken);

        return ResultFactory.Failure<TResponse>(new Error("PeriodLock.Locked", $"Period {periodSensitiveRequest.OperationDate:yyyy-MM} is locked for module {periodSensitiveRequest.Module}."));
    }
}

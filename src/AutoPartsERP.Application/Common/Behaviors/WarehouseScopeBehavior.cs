using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Application.Common.Abstractions.Markers;
using AutoPartsERP.Application.Common.Models;

namespace AutoPartsERP.Application.Common.Behaviors;

/// <summary>
/// Keeps warehouse staff to the warehouses assigned to them. Holders of <c>inventory:all_warehouses</c> (and SYSTEM_ADMIN, who holds every
/// permission) pass; so does an approved request being replayed (the requester was checked when it was submitted).
/// </summary>
public sealed class WarehouseScopeBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : class
{
    private readonly ICurrentUser _currentUser;
    private readonly IWarehouseScope _scope;
    private readonly IApprovalReplayContext _replayContext;

    public WarehouseScopeBehavior(ICurrentUser currentUser, IWarehouseScope scope, IApprovalReplayContext replayContext)
    {
        _currentUser = currentUser;
        _scope = scope;
        _replayContext = replayContext;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IWarehouseScopedRequest scoped
            || _replayContext.IsReplaying
            || _currentUser.HasPermission(PermissionCodes.Inventory.AllWarehouses))
        {
            return await next();
        }

        var touch = await _scope.ResolveAsync(scoped, cancellationToken);
        if (touch is null || await _scope.CanSeeAsync(_currentUser.UserId, touch, cancellationToken))
        {
            return await next();
        }

        return ResultFactory.Failure<TResponse>(new Error("Authorization.WarehouseNotAssigned", "This warehouse is not assigned to you."));
    }
}

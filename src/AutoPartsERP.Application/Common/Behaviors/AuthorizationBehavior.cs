using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Application.Common.Abstractions.Markers;
using AutoPartsERP.Application.Common.Models;

namespace AutoPartsERP.Application.Common.Behaviors;

public sealed class AuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : class
{
    private readonly ICurrentUser _currentUser;
    private readonly IManualAuditService _audit;

    public AuthorizationBehavior(ICurrentUser currentUser, IManualAuditService audit)
    {
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IAuthorizedRequest authorizedRequest)
        {
            return await next();
        }

        if (_currentUser.HasPermission(authorizedRequest.RequiredPermission))
        {
            return await next();
        }

        await _audit.LogRejectionAsync(
            new RejectionEntry(
                _currentUser.CorrelationId,
                _currentUser.UserId,
                _currentUser.Username,
                typeof(TRequest).Name,
                authorizedRequest.RequiredPermission,
                "Insufficient permissions.",
                _currentUser.IpAddress),
            cancellationToken);

        return ResultFactory.Failure<TResponse>(new Error("Authorization.Forbidden", $"Permission '{authorizedRequest.RequiredPermission}' is required."));
    }
}

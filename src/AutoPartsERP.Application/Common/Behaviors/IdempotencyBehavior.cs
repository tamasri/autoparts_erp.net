using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Application.Common.Abstractions.Markers;
using AutoPartsERP.Application.Common.Models;

namespace AutoPartsERP.Application.Common.Behaviors;

public sealed class IdempotencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : class
{
    private readonly IIdempotencyService _idempotency;
    private readonly ICurrentUser _currentUser;
    private readonly IManualAuditService _audit;

    public IdempotencyBehavior(IIdempotencyService idempotency, ICurrentUser currentUser, IManualAuditService audit)
    {
        _idempotency = idempotency;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IIdempotentRequest idempotentRequest)
        {
            return await next();
        }

        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        var checkResult = await _idempotency.CheckAsync(idempotentRequest.IdempotencyKey, _currentUser.UserId, typeof(TRequest).Name, requestHash, cancellationToken);

        if (checkResult.IsReplay && checkResult.CachedResponse is not null)
        {
            await _audit.LogAsync(
                new ManualAuditEntry(
                    _currentUser.CorrelationId,
                    AuditActions.IdempotencyReplay,
                    "SYSTEM",
                    typeof(TRequest).Name,
                    null,
                    _currentUser.UserId,
                    _currentUser.Username,
                    "REPLAY",
                    "SUCCESS",
                    IpAddress: _currentUser.IpAddress,
                    UserAgent: _currentUser.UserAgent),
                cancellationToken);

            var replayed = JsonSerializer.Deserialize<TResponse>(checkResult.CachedResponse);
            return replayed ?? ResultFactory.Failure<TResponse>(new Error("Idempotency.Deserialize", "The stored idempotent response could not be restored."));
        }

        if (checkResult.IsConflict)
        {
            return ResultFactory.Failure<TResponse>(new Error("Idempotency.Conflict", "Another request with the same idempotency key is currently being processed."));
        }

        try
        {
            var response = await next();
            await _idempotency.CompleteAsync(idempotentRequest.IdempotencyKey, _currentUser.UserId, response, cancellationToken);
            return response;
        }
        catch
        {
            await _idempotency.FailAsync(idempotentRequest.IdempotencyKey, _currentUser.UserId, cancellationToken);
            throw;
        }
    }
}

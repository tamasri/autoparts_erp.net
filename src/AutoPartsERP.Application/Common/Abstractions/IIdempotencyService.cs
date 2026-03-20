using AutoPartsERP.Application.Common.Models;

namespace AutoPartsERP.Application.Common.Abstractions;

public interface IIdempotencyService
{
    Task<IdempotencyCheckResult> CheckAsync(string key, Guid userId, string endpoint, string requestHash, CancellationToken cancellationToken = default);

    Task CompleteAsync(string key, Guid userId, object response, CancellationToken cancellationToken = default);

    Task FailAsync(string key, Guid userId, CancellationToken cancellationToken = default);
}

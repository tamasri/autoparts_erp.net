namespace AutoPartsERP.Application.Common.Abstractions;

public interface IPeriodLockService
{
    Task<bool> IsLockedAsync(int year, int month, string module, CancellationToken cancellationToken = default);

    Task InvalidateCacheAsync(int year, int month, string module, CancellationToken cancellationToken = default);
}

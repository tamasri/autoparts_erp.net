namespace AutoPartsERP.Application.Common.Abstractions;

/// <summary>The locations a request touches. With <c>AnyIsEnough</c> (transfers) one visible side is enough; otherwise every location must be visible.</summary>
public sealed record WarehouseTouch(IReadOnlyList<Guid> Locations, bool AnyIsEnough);

public interface IWarehouseScope
{
    /// <summary>Null when the document does not exist (the handler then answers "not found").</summary>
    Task<WarehouseTouch?> ResolveAsync(IWarehouseScopedRequest request, CancellationToken cancellationToken = default);

    Task<bool> CanSeeAsync(Guid userId, WarehouseTouch touch, CancellationToken cancellationToken = default);
}

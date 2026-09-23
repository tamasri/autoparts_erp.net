using AutoPartsERP.Application.Common.Abstractions.Markers;

namespace AutoPartsERP.Application.Common.Abstractions;

/// <summary>The two warehouses (top-level locations) a transfer moves stock between, and the key of the document it concerns.</summary>
public sealed record TransferScope(Guid SourceWarehouseId, Guid DestinationWarehouseId, string EntityId)
{
    public IReadOnlyList<Guid> Warehouses => SourceWarehouseId == DestinationWarehouseId ? [SourceWarehouseId] : [SourceWarehouseId, DestinationWarehouseId];
}

/// <summary>Which warehouses a user works in and manages (assigned by an administrator in <c>user_warehouses</c>).</summary>
public interface IWarehouseAccess
{
    /// <summary>Warehouses the user is the manager of. Empty for everyone without a manager assignment.</summary>
    Task<IReadOnlySet<Guid>> ManagedWarehousesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The warehouses a transfer request moves stock between; null when both ends are in the same warehouse (no approval needed).</summary>
    Task<Result<TransferScope?>> ResolveTransferAsync(IWarehouseTransferRequest request, CancellationToken cancellationToken = default);
}

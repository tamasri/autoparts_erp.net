using AutoPartsERP.Application.Features.Inventory.TransferStock;
using AutoPartsERP.Application.Features.Transfers;

namespace AutoPartsERP.Application.Features.Wms;

/// <summary>
/// Reads the user ↔ warehouse assignments and works out which warehouses a transfer touches. A "warehouse" is a top-level location: a shelf or a
/// zone belongs to the warehouse at the top of its parent chain, so a move between two shelves of one warehouse is not a transfer.
/// </summary>
public sealed class WarehouseAccess : IWarehouseAccess
{
    private readonly IDbConnectionFactory _connectionFactory;

    public WarehouseAccess(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<IReadOnlySet<Guid>> ManagedWarehousesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT warehouse_id FROM user_warehouses WHERE user_id = @userId AND is_manager;", new { userId }, cancellationToken: cancellationToken))).ToHashSet();
    }

    public async Task<Result<TransferScope?>> ResolveTransferAsync(IWarehouseTransferRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        // The entity key identifies the document: a second request to ship the same order while one is pending is refused.
        (Guid From, Guid To, string Key)? ends = null;
        switch (request)
        {
            case ShipTransferOrderCommand ship:
                var order = (await connection.QueryAsync<(Guid From, Guid To)>(new CommandDefinition(
                    "SELECT source_warehouse_id, destination_warehouse_id FROM transfer_orders WHERE id = @TransferOrderId;",
                    new { ship.TransferOrderId }, cancellationToken: cancellationToken))).ToList();
                ends = order.Count == 0 ? null : (order[0].From, order[0].To, ship.TransferOrderId.ToString());
                break;
            case TransferStockCommand move:
                ends = (move.FromLocationId, move.ToLocationId, $"stock:{move.IdempotencyKey}");
                break;
            case CreateTransferRequestCommand plan:
                ends = (plan.SourceWarehouseId, plan.DestinationWarehouseId, $"request:{Guid.NewGuid()}");
                break;
        }
        if (ends is null)
        {
            return Result<TransferScope?>.Failure(new Error("Transfer.NotFound", "The transfer was not found."));
        }

        var roots = (await connection.QueryAsync<(Guid Start, Guid Root)>(new CommandDefinition(
            """
            WITH RECURSIVE up AS (
                SELECT id, parent_id, id AS start FROM locations WHERE id = ANY(@ids)
                UNION ALL
                SELECT l.id, l.parent_id, up.start FROM locations l INNER JOIN up ON l.id = up.parent_id
            )
            SELECT start AS Start, id AS Root FROM up WHERE parent_id IS NULL;
            """,
            new { ids = new[] { ends.Value.From, ends.Value.To } }, cancellationToken: cancellationToken))).ToDictionary(x => x.Start, x => x.Root);

        if (!roots.TryGetValue(ends.Value.From, out var source) || !roots.TryGetValue(ends.Value.To, out var destination))
        {
            return Result<TransferScope?>.Failure(new Error("Transfer.InvalidWarehouses", "A location of the transfer does not exist."));
        }

        return Result<TransferScope?>.Success(source == destination ? null : new TransferScope(source, destination, ends.Value.Key));
    }
}

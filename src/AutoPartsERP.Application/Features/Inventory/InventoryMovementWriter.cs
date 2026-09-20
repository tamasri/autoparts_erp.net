using System.Data.Common;
using Dapper;

namespace AutoPartsERP.Application.Features.Inventory;

/// <summary>
/// The item-movement ledger (<c>inventory_movements</c>): one row for every change of stock, whichever module made it.
/// The operational stock table is keyed by SKU while the ledger is keyed by item, so the item is resolved through
/// <c>items.sku_id</c>. A SKU that has no item row yet has no ledger row until the catalogue sync creates it.
/// </summary>
internal static class InventoryMovementWriter
{
    public static Task RecordAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid skuId,
        Guid locationId,
        Guid? batchId,
        decimal quantity,
        bool isIn,
        string movementType,
        string referenceType,
        Guid? referenceId,
        Guid performedBy,
        string? notes,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO inventory_movements (
                id, item_id, location_id, batch_id, movement_type, qty, direction,
                from_status, to_status, reference_type, reference_id, performed_by, correlation_id, notes, created_at)
            SELECT uuid_generate_v4(), i.id, @locationId, @batchId, @movementType, @quantity, CASE WHEN @isIn THEN 'IN' ELSE 'OUT' END,
                   CASE WHEN @isIn THEN NULL ELSE 'AVAILABLE' END, CASE WHEN @isIn THEN 'AVAILABLE' ELSE NULL END,
                   @referenceType, @referenceId, @performedBy, uuid_generate_v4(), @notes, now()
            FROM items i
            WHERE i.sku_id = @skuId AND @quantity > 0;
            """,
            new { skuId, locationId, batchId, quantity, isIn, movementType, referenceType, referenceId, performedBy, notes },
            transaction,
            cancellationToken: cancellationToken));
}

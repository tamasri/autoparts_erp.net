using System.Data.Common;
using AutoPartsERP.Application.Features.Inventory;
using Dapper;

namespace AutoPartsERP.Application.Features.Invoices;

internal enum StockDirection
{
    Out,
    In
}

/// <summary>
/// Moves inventory for an invoice line. One implementation shared by posting and voiding so the two can never disagree:
/// a sale takes stock Out, a customer return brings it In, and a void does the opposite of the original posting.
/// The sellable stock (<c>inventory_stock</c>) and the warehouse view (<c>inventory_balances</c>, the item's un-batched AVAILABLE row)
/// move together in the caller's transaction, so warehouse screens show a sale at once instead of after the next balances sync.
/// </summary>
internal static class InvoiceStockMover
{
    public static async Task<Result> MoveAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid invoiceId,
        Guid skuId,
        Guid locationId,
        Guid? batchId,
        decimal quantity,
        StockDirection direction,
        Guid performedBy,
        string note,
        CancellationToken cancellationToken)
    {
        if (direction == StockDirection.Out)
        {
            // Reserved goods are not for sale: the sale may only take what is on hand and not reserved (same rule as StockLevelWriter).
            var taken = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE inventory_stock SET quantity_on_hand = quantity_on_hand - @quantity, updated_at = now()
                WHERE sku_id = @skuId AND location_id = @locationId AND quantity_on_hand - @quantity >= quantity_reserved;
                """,
                new { quantity, skuId, locationId }, transaction, cancellationToken: cancellationToken));
            if (taken == 0)
            {
                return Result.Failure(new Error("Stock.InsufficientQuantity", "Insufficient sellable stock at the location."));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE inventory_balances b SET qty = GREATEST(b.qty - @quantity, 0), updated_at = now()
                FROM items i
                WHERE i.sku_id = @skuId AND b.item_id = i.id AND b.location_id = @locationId AND b.batch_id IS NULL AND b.status = 'AVAILABLE';
                """,
                new { quantity, skuId, locationId }, transaction, cancellationToken: cancellationToken));
        }
        else
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO inventory_stock (sku_id, location_id, quantity_on_hand, quantity_reserved, updated_at)
                VALUES (@skuId, @locationId, @quantity, 0, now())
                ON CONFLICT (sku_id, location_id) DO UPDATE
                    SET quantity_on_hand = inventory_stock.quantity_on_hand + EXCLUDED.quantity_on_hand, updated_at = now();
                """,
                new { skuId, locationId, quantity }, transaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO inventory_balances (id, item_id, location_id, batch_id, status, qty, updated_at)
                SELECT uuid_generate_v4(), i.id, @locationId, NULL, 'AVAILABLE', @quantity, now() FROM items i WHERE i.sku_id = @skuId
                ON CONFLICT (item_id, location_id, batch_id, status) DO UPDATE SET qty = inventory_balances.qty + EXCLUDED.qty, updated_at = now();
                """,
                new { skuId, locationId, quantity }, transaction, cancellationToken: cancellationToken));
        }

        await InventoryMovementWriter.RecordAsync(
            connection, transaction, skuId, locationId, batchId, quantity, direction == StockDirection.In,
            direction == StockDirection.In ? "SALE_RETURN" : "SALE", "INVOICE", invoiceId, performedBy, note, cancellationToken);

        if (batchId is null)
        {
            return Result.Success();
        }

        // batch_movements.batch_id is NOT NULL: only batch-tracked lines have a batch to update and a movement to record.
        if (direction == StockDirection.Out)
        {
            var current = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                "SELECT quantity_current FROM batches WHERE id = @batchId FOR UPDATE;",
                new { batchId }, transaction, cancellationToken: cancellationToken));
            if (current is not null && current < quantity)
            {
                return Result.Failure(new Error("Stock.InsufficientQuantity", "Insufficient batch quantity."));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE batches SET quantity_current = quantity_current - @quantity, status = CASE WHEN quantity_current - @quantity = 0 THEN 'DEPLETED' ELSE status END WHERE id = @batchId;",
                new { quantity, batchId }, transaction, cancellationToken: cancellationToken));
        }
        else
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE batches SET quantity_current = quantity_current + @quantity, status = CASE WHEN status = 'DEPLETED' THEN 'ACTIVE' ELSE status END WHERE id = @batchId;",
                new { quantity, batchId }, transaction, cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO batch_movements (
                id, batch_id, movement_type, quantity, direction, reference_type, reference_id,
                from_location_id, to_location_id, unit_cost_syp, unit_cost_usd, performed_by, notes, created_at)
            VALUES (uuid_generate_v4(), @batchId, @movementType, @quantity, @direction, 'INVOICE', @invoiceId,
                @fromLocation, @toLocation, 0, 0, @performedBy, @note, now());
            """,
            new
            {
                batchId,
                movementType = direction == StockDirection.Out ? "INVOICE_OUT" : "RETURN_IN",
                quantity,
                direction = direction == StockDirection.Out ? "OUT" : "IN",
                invoiceId,
                fromLocation = direction == StockDirection.Out ? locationId : (Guid?)null,
                toLocation = direction == StockDirection.In ? locationId : (Guid?)null,
                performedBy,
                note
            },
            transaction,
            cancellationToken: cancellationToken));

        return Result.Success();
    }
}

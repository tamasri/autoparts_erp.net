using System.Data.Common;
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
            var onHand = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                "SELECT quantity_on_hand FROM inventory_stock WHERE sku_id = @skuId AND location_id = @locationId FOR UPDATE;",
                new { skuId, locationId }, transaction, cancellationToken: cancellationToken));
            if (onHand is null || onHand < quantity)
            {
                return Result.Failure(new Error("Stock.InsufficientQuantity", "Insufficient stock quantity."));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE inventory_stock SET quantity_on_hand = quantity_on_hand - @quantity, updated_at = now() WHERE sku_id = @skuId AND location_id = @locationId;",
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
        }

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

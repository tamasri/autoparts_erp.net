using System.Data.Common;
using Dapper;

namespace AutoPartsERP.Application.Features.Inventory;

/// <summary>
/// Keeps the sellable stock table (<c>inventory_stock</c>, by SKU) in step with the warehouse documents (which work on
/// <c>inventory_balances</c>, by item and status). Whenever a warehouse document changes the AVAILABLE quantity of an item at a
/// location (putaway, transfer shipped/received, an adjustment) it calls this in the same transaction, so goods that are
/// received through a document can be sold at once and goods that are shipped can no longer be sold.
/// An item without a SKU is not sold and has no stock row to move.
/// </summary>
internal static class StockLevelWriter
{
    /// <returns>Success, or <c>Stock.InsufficientQuantity</c> when a decrease would take the location below zero.</returns>
    public static async Task<Result> ApplyAvailableAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid itemId,
        Guid locationId,
        decimal delta,
        CancellationToken cancellationToken)
    {
        if (delta == 0)
        {
            return Result.Success();
        }

        var skuId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT sku_id FROM items WHERE id = @itemId;", new { itemId }, transaction, cancellationToken: cancellationToken));
        if (skuId is null)
        {
            return Result.Success();
        }

        if (delta > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO inventory_stock (sku_id, location_id, quantity_on_hand, quantity_reserved, updated_at)
                VALUES (@skuId, @locationId, @delta, 0, now())
                ON CONFLICT (sku_id, location_id) DO UPDATE
                    SET quantity_on_hand = inventory_stock.quantity_on_hand + EXCLUDED.quantity_on_hand, updated_at = now();
                """,
                new { skuId, locationId, delta }, transaction, cancellationToken: cancellationToken));
            return Result.Success();
        }

        var changed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE inventory_stock
            SET quantity_on_hand = quantity_on_hand + @delta, updated_at = now()
            WHERE sku_id = @skuId AND location_id = @locationId AND quantity_on_hand + @delta >= quantity_reserved;
            """,
            new { skuId, locationId, delta }, transaction, cancellationToken: cancellationToken));

        return changed == 0
            ? Result.Failure(new Error("Stock.InsufficientQuantity", "Insufficient sellable stock at the location."))
            : Result.Success();
    }
}

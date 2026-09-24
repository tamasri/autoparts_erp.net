using System.Data.Common;
using Dapper;

namespace AutoPartsERP.Application.Features.Inventory;

/// <summary>
/// The moving (weighted) average cost of a SKU over everything on hand in every warehouse — one implementation for every movement
/// that carries a known unit cost. Call it BEFORE the stock itself moves: the average is taken over what was on hand until then.
/// <list type="bullet">
/// <item><see cref="BlendInAsync"/>: goods arrive at a cost (a purchase, a customer return at the cost it was sold at, a voided sale
/// coming back).</item>
/// <item><see cref="BlendOutAsync"/>: goods leave at the cost they came in with (a purchase return, a voided purchase, a voided
/// customer return): the average goes back to what it was without them. A sale does not change the average and does not call this.</item>
/// </list>
/// The lira cost follows the dollar cost at the document's rate.
/// </summary>
internal static class InventoryCosting
{
    public static async Task BlendInAsync(
        DbConnection connection, DbTransaction transaction, Guid skuId, decimal quantity, decimal unitCostUsd, decimal fxRate, Guid by, CancellationToken ct)
    {
        var (onHand, cost) = await CurrentAsync(connection, transaction, skuId, ct);
        var held = Math.Max(onHand, 0m);
        var newCost = held + quantity <= 0 ? unitCostUsd : Math.Round((held * cost + quantity * unitCostUsd) / (held + quantity), 4);
        await SetAsync(connection, transaction, skuId, newCost, fxRate, by, ct);
    }

    public static async Task BlendOutAsync(
        DbConnection connection, DbTransaction transaction, Guid skuId, decimal quantity, decimal unitCostUsd, decimal fxRate, Guid by, CancellationToken ct)
    {
        var (onHand, cost) = await CurrentAsync(connection, transaction, skuId, ct);
        var remaining = onHand - quantity;
        if (remaining <= 0)
        {
            return; // nothing left to carry an average; the last cost stays as the reference
        }

        var newCost = Math.Max(Math.Round((onHand * cost - quantity * unitCostUsd) / remaining, 4), 0m);
        await SetAsync(connection, transaction, skuId, newCost, fxRate, by, ct);
    }

    private static Task<(decimal OnHand, decimal Cost)> CurrentAsync(DbConnection connection, DbTransaction transaction, Guid skuId, CancellationToken ct) =>
        connection.QuerySingleAsync<(decimal OnHand, decimal Cost)>(new CommandDefinition(
            """
            SELECT COALESCE((SELECT SUM(quantity_on_hand) FROM inventory_stock WHERE sku_id = @skuId), 0) AS OnHand,
                   (SELECT cost_price_usd FROM skus WHERE id = @skuId FOR UPDATE) AS Cost;
            """,
            new { skuId }, transaction, cancellationToken: ct));

    private static Task SetAsync(DbConnection connection, DbTransaction transaction, Guid skuId, decimal costUsd, decimal fxRate, Guid by, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            "UPDATE skus SET cost_price_usd = @costUsd, cost_price_syp = round(@costUsd * @fxRate, 4), updated_at = now(), updated_by = @by WHERE id = @skuId;",
            new { skuId, costUsd, fxRate, by }, transaction, cancellationToken: ct));
}

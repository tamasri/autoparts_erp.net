using Dapper;

namespace AutoPartsERP.Application.Features.InventoryAlerts;

public static class StockAlertTypes
{
    public const string LowStock = "LOW_STOCK";
    public const string OutOfStock = "OUT_OF_STOCK";
}

/// <summary>An alert this scan raised (the ones a person should be told about now).</summary>
public sealed record RaisedStockAlert(Guid AlertId, Guid ItemId, string Code, string Name, string AlertType, string Severity, decimal Available, decimal ReorderLevel);

/// <summary>
/// Keeps <c>inventory_alerts</c> true to the stock: an item with a reorder level gets one open alert while its available quantity
/// (all warehouses, reserved excluded) is at or below that level — OUT_OF_STOCK when nothing is left — and the alert resolves itself
/// when stock is back above the level. Items without a reorder level are not watched. Run after sales (for the items sold) and
/// periodically for everything (transfers, adjustments, counts).
/// </summary>
public static class StockAlertScanner
{
    public sealed record Decision(string? Raise, string? Severity, IReadOnlyList<string> Resolve);

    /// <summary>What should change for one item, given its availability, its reorder level and its currently open alert types.</summary>
    public static Decision Decide(decimal available, decimal reorderLevel, IReadOnlyCollection<string> open)
    {
        string? wanted = reorderLevel <= 0 ? null
            : available <= 0 ? StockAlertTypes.OutOfStock
            : available <= reorderLevel ? StockAlertTypes.LowStock
            : null;
        var resolve = open.Where(t => t != wanted).ToList();
        var raise = wanted is not null && !open.Contains(wanted) ? wanted : null;
        var severity = raise == StockAlertTypes.OutOfStock ? "CRITICAL" : raise == StockAlertTypes.LowStock ? "HIGH" : null;
        return new Decision(raise, severity, resolve);
    }

    private sealed record Level(Guid ItemId, string Code, string Name, decimal ReorderLevel, decimal Available);

    private sealed record OpenAlert(Guid Id, Guid ItemId, string AlertType);

    /// <param name="skuIds">Only these SKUs (e.g. the lines of a posted invoice); null = every item.</param>
    public static async Task<IReadOnlyList<RaisedStockAlert>> ScanAsync(IDbConnection connection, IReadOnlyCollection<Guid>? skuIds, CancellationToken cancellationToken)
    {
        var all = skuIds is null;
        var ids = skuIds?.ToArray() ?? [];
        var levels = (await connection.QueryAsync<Level>(new CommandDefinition(
            """
            SELECT i.id AS ItemId, s.code AS Code, COALESCE(NULLIF(i.name_ar, ''), i.name_en, s.name) AS Name, i.reorder_level AS ReorderLevel,
                   COALESCE((SELECT sum(st.quantity_available) FROM inventory_stock st WHERE st.sku_id = i.sku_id), 0) AS Available
            FROM items i INNER JOIN skus s ON s.id = i.sku_id
            WHERE i.is_active AND (@all OR i.sku_id = ANY(@ids));
            """,
            new { all, ids }, cancellationToken: cancellationToken))).ToList();
        if (levels.Count == 0)
        {
            return [];
        }

        var open = (await connection.QueryAsync<OpenAlert>(new CommandDefinition(
            """
            SELECT id AS Id, item_id AS ItemId, alert_type AS AlertType FROM inventory_alerts
            WHERE status <> 'RESOLVED' AND alert_type IN ('LOW_STOCK', 'OUT_OF_STOCK') AND item_id = ANY(@items);
            """,
            new { items = levels.Select(l => l.ItemId).ToArray() }, cancellationToken: cancellationToken))).ToLookup(a => a.ItemId);

        var raised = new List<RaisedStockAlert>();
        foreach (var item in levels)
        {
            var current = open[item.ItemId].ToList();
            var decision = Decide(item.Available, item.ReorderLevel, current.Select(a => a.AlertType).ToList());

            foreach (var type in decision.Resolve)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE inventory_alerts SET status = 'RESOLVED', resolved_at = now(), resolution_note = @note, current_value = @Available
                    WHERE item_id = @ItemId AND alert_type = @type AND status <> 'RESOLVED';
                    """,
                    new { item.ItemId, item.Available, type, note = item.Available > item.ReorderLevel ? "عاد المخزون فوق حد إعادة الطلب" : "تغيّرت حالة المخزون" },
                    cancellationToken: cancellationToken));
            }

            if (decision.Raise is { } raise)
            {
                var message = raise == StockAlertTypes.OutOfStock
                    ? $"نفد المخزون: {item.Name} ({item.Code})"
                    : $"مخزون منخفض: {item.Name} ({item.Code}) — المتاح {item.Available:0.##} والحد {item.ReorderLevel:0.##}";
                // The partial unique index keeps one open alert per item and type even if two scans race.
                var id = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                    """
                    INSERT INTO inventory_alerts (id, item_id, alert_type, severity, message, threshold_value, current_value, status, created_at)
                    VALUES (uuid_generate_v4(), @ItemId, @raise, @Severity, @message, @ReorderLevel, @Available, 'OPEN', now())
                    ON CONFLICT (item_id, alert_type) WHERE status <> 'RESOLVED' DO NOTHING
                    RETURNING id;
                    """,
                    new { item.ItemId, raise, decision.Severity, message, item.ReorderLevel, item.Available }, cancellationToken: cancellationToken));
                if (id is { } alertId)
                {
                    raised.Add(new RaisedStockAlert(alertId, item.ItemId, item.Code, item.Name, raise, decision.Severity!, item.Available, item.ReorderLevel));
                }
            }
            else
            {
                // Still open: keep the figure current.
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE inventory_alerts SET current_value = @Available WHERE item_id = @ItemId AND status <> 'RESOLVED' AND current_value IS DISTINCT FROM @Available;",
                    new { item.ItemId, item.Available }, cancellationToken: cancellationToken));
            }
        }

        return raised;
    }

    /// <summary>Pushes one notice for everything a scan raised (to users who can see stock alerts).</summary>
    public static Task NotifyAsync(IRealtimeNotifier notifier, IReadOnlyList<RaisedStockAlert> raised, CancellationToken cancellationToken) =>
        raised.Count == 0
            ? Task.CompletedTask
            : notifier.ToPermissionAsync(PermissionCodes.InventoryAlerts.Read, RealtimeEvents.StockAlert, new
            {
                count = raised.Count,
                outOfStock = raised.Count(r => r.AlertType == StockAlertTypes.OutOfStock),
                items = raised.Take(5).Select(r => new { r.ItemId, r.Code, r.Name, r.AlertType, r.Available, r.ReorderLevel }),
            }, cancellationToken);
}

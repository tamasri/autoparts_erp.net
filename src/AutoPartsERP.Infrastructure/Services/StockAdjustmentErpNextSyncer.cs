namespace AutoPartsERP.Infrastructure.Services;

/// <summary>
/// Books the value of a posted stock adjustment in ERPNext so its inventory account keeps matching the stock this application holds.
/// A shortage or damage is a cost (Dr Cost of Goods Sold / Cr Inventory); a surplus found is the reverse. Value = quantity change at the
/// item's current cost. An adjustment worth nothing (or of goods with no cost yet) is logged as SKIPPED, not sent.
/// </summary>
public sealed class StockAdjustmentErpNextSyncer
{
    private const string Entity = "StockAdjustment";
    private const string Doctype = "Journal Entry";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IErpNextClient _erpNextClient;

    public StockAdjustmentErpNextSyncer(IDbConnectionFactory connectionFactory, IErpNextClient erpNextClient)
    {
        _connectionFactory = connectionFactory;
        _erpNextClient = erpNextClient;
    }

    public async Task SyncAsync(Guid adjustmentId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        if (await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, Entity, adjustmentId, Doctype, cancellationToken) is not null)
        {
            return;
        }

        var adjustment = await connection.QuerySingleOrDefaultAsync<(string AdjustmentNo, DateOnly Date, decimal Value)>(new CommandDefinition(
            """
            SELECT a.adjustment_no AS AdjustmentNo, COALESCE(a.posted_at, a.created_at)::date AS Date,
                   COALESCE(SUM(l.qty_delta * k.cost_price_usd), 0) AS Value
            FROM stock_adjustments a
            LEFT JOIN stock_adjustment_lines l ON l.stock_adjustment_id = a.id AND l.status = 'AVAILABLE'
            LEFT JOIN items i ON i.id = l.item_id
            LEFT JOIN skus k ON k.id = i.sku_id
            WHERE a.id = @adjustmentId AND a.status = 'POSTED'
            GROUP BY a.id;
            """,
            new { adjustmentId }, cancellationToken: cancellationToken));
        if (adjustment.AdjustmentNo is null)
        {
            return;
        }

        if (Math.Abs(adjustment.Value) < 0.005m)
        {
            await ErpNextSyncLogWriter.WriteAsync(connection, Entity, adjustmentId, Doctype, null, ErpNextSyncLogWriter.Skipped,
                "The adjustment has no monetary value (zero cost or no available-stock lines).", cancellationToken);
            return;
        }

        var result = await _erpNextClient.SyncCogsEntryAsync(
            new ErpNextCogsEntrySync(adjustmentId, adjustment.AdjustmentNo, adjustment.Date, Math.Abs(adjustment.Value), adjustment.Value > 0,
                $"Inventory adjustment {adjustment.AdjustmentNo} from AutoPartsERP"),
            cancellationToken);

        await ErpNextSyncLogWriter.WriteAsync(connection, Entity, adjustmentId, Doctype, result.IsSuccess ? result.Value : null,
            _erpNextClient.IsEnabled ? (result.IsSuccess ? ErpNextSyncLogWriter.Synced : ErpNextSyncLogWriter.Failed) : ErpNextSyncLogWriter.Skipped,
            result.IsFailure ? result.Error.Message : null, cancellationToken);
    }

    /// <summary>Posted adjustments ERPNext does not know about yet (or whose hand-off failed).</summary>
    public async Task<IReadOnlyList<Guid>> FindPendingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        return (await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT a.id FROM stock_adjustments a
            WHERE a.status = 'POSTED'
              AND NOT EXISTS (SELECT 1 FROM erpnext_sync_log l WHERE l.local_entity_type = 'StockAdjustment' AND l.local_entity_id = a.id
                                AND l.erpnext_doctype = 'Journal Entry' AND l.status IN ('SYNCED', 'SKIPPED'))
            ORDER BY a.posted_at;
            """,
            cancellationToken: cancellationToken))).ToList();
    }
}

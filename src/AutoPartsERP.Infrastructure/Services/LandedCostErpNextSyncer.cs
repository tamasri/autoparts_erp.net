namespace AutoPartsERP.Infrastructure.Services;

/// <summary>
/// Books a posted landed cost voucher in ERPNext as one Journal Entry — Dr inventory for what was capitalized, Dr cost of goods sold
/// for the part of goods already sold, Cr the landed-cost clearing account for what suppliers billed (their service bills debited
/// it), Cr each cash/bank account a charge was paid from — and cancels it when the voucher is voided. Logged in erpnext_sync_log.
/// </summary>
public sealed class LandedCostErpNextSyncer
{
    private const string Entity = "LandedCost";
    private const string Doctype = "Journal Entry";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IErpNextClient _erpNextClient;

    public LandedCostErpNextSyncer(IDbConnectionFactory connectionFactory, IErpNextClient erpNextClient)
    {
        _connectionFactory = connectionFactory;
        _erpNextClient = erpNextClient;
    }

    private string StatusOf(bool succeeded) =>
        _erpNextClient.IsEnabled ? (succeeded ? ErpNextSyncLogWriter.Synced : ErpNextSyncLogWriter.Failed) : ErpNextSyncLogWriter.Skipped;

    public async Task SyncAsync(Guid voucherId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        if (await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, Entity, voucherId, Doctype, cancellationToken) is not null)
        {
            return;
        }

        var voucher = await connection.QuerySingleOrDefaultAsync<(string Number, DateOnly Date)?>(new CommandDefinition(
            "SELECT voucher_number AS Number, voucher_date AS Date FROM landed_cost_vouchers WHERE id = @voucherId AND status = 'POSTED';",
            new { voucherId }, cancellationToken: cancellationToken));
        if (voucher is not { } v)
        {
            return;
        }

        var totals = await connection.QuerySingleAsync<(decimal Capitalized, decimal Expensed)>(new CommandDefinition(
            "SELECT COALESCE(SUM(capitalized_usd), 0) AS Capitalized, COALESCE(SUM(expensed_usd), 0) AS Expensed FROM landed_cost_item_effects WHERE voucher_id = @voucherId;",
            new { voucherId }, cancellationToken: cancellationToken));
        var billed = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(SUM(amount_usd), 0) FROM landed_cost_charges WHERE voucher_id = @voucherId AND supplier_party_id IS NOT NULL;",
            new { voucherId }, cancellationToken: cancellationToken));
        var paid = (await connection.QueryAsync<ErpNextPaidCharge>(new CommandDefinition(
            """
            SELECT paid_from_account AS Account, SUM(amount_usd) AS Amount FROM landed_cost_charges
            WHERE voucher_id = @voucherId AND paid_from_account IS NOT NULL GROUP BY paid_from_account ORDER BY paid_from_account;
            """,
            new { voucherId }, cancellationToken: cancellationToken))).ToList();

        var result = await _erpNextClient.SyncLandedCostEntryAsync(
            new ErpNextLandedCostSync(voucherId, v.Number, v.Date, totals.Capitalized, totals.Expensed, billed, paid), cancellationToken);
        await ErpNextSyncLogWriter.WriteAsync(connection, Entity, voucherId, Doctype, result.IsSuccess ? result.Value : null,
            StatusOf(result.IsSuccess), result.IsFailure ? result.Error.Message : null, cancellationToken);
    }

    /// <summary>Cancels the Journal Entry when the voucher is voided; a voucher that never got there needs nothing.</summary>
    public async Task CancelAsync(Guid voucherId, CancellationToken cancellationToken)
    {
        if (!_erpNextClient.IsEnabled)
        {
            return;
        }

        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        var name = await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, Entity, voucherId, Doctype, cancellationToken);
        if (name is null)
        {
            return;
        }

        var result = await _erpNextClient.CancelDocumentAsync(Doctype, name, cancellationToken);
        await ErpNextSyncLogWriter.WriteAsync(connection, Entity, voucherId, Doctype, name,
            result.IsSuccess ? ErpNextSyncLogWriter.Cancelled : ErpNextSyncLogWriter.Failed,
            result.IsFailure ? $"Cancel failed: {result.Error.Message}" : null, cancellationToken);
    }

    /// <summary>Posted vouchers ERPNext does not have yet (or whose hand-off failed).</summary>
    public async Task<IReadOnlyList<Guid>> FindPendingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        return (await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT v.id FROM landed_cost_vouchers v
            WHERE v.status = 'POSTED'
              AND NOT EXISTS (SELECT 1 FROM erpnext_sync_log l WHERE l.local_entity_type = 'LandedCost' AND l.local_entity_id = v.id
                                AND l.erpnext_doctype = 'Journal Entry' AND l.status IN ('SYNCED', 'SKIPPED', 'CANCELLED'))
            ORDER BY v.voucher_date, v.serial_no;
            """,
            cancellationToken: cancellationToken))).ToList();
    }
}

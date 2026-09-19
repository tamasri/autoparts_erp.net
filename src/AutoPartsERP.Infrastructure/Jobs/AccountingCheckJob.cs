namespace AutoPartsERP.Infrastructure.Jobs;

/// <summary>
/// Daily accounting review. Rule-based (SQL) on purpose: it reports facts that can be verified, and it records
/// exactly what it found in ai_task_runs. (An earlier version inserted a hardcoded "COMPLETED — generated
/// suggestions" row without checking anything.) LLM narration and suggestion generation are Phase 6 work and will
/// sit on top of these same findings.
/// </summary>
public sealed class AccountingCheckJob
{
    private const string TaskCode = "ACCOUNTING_CHECK";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<AccountingCheckJob> _logger;

    public AccountingCheckJob(IDbConnectionFactory connectionFactory, ILogger<AccountingCheckJob> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    [Queue("governance")]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);

        try
        {
            var overdue = await connection.QuerySingleAsync<(int Count, decimal Usd)>(new CommandDefinition(
                """
                SELECT COUNT(*)::int AS Count, COALESCE(SUM(balance_usd), 0) AS Usd
                FROM invoices
                WHERE status = 'POSTED' AND invoice_type = 'SALE' AND balance_syp > 0 AND due_date < CURRENT_DATE;
                """,
                cancellationToken: cancellationToken));

            var overLimit = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(*)::int FROM (
                    SELECT c.id
                    FROM customers c
                    INNER JOIN invoices i ON i.customer_id = c.id AND i.status = 'POSTED' AND i.invoice_type = 'SALE'
                    WHERE c.credit_limit_usd > 0
                    GROUP BY c.id, c.credit_limit_usd
                    HAVING SUM(i.balance_usd) > c.credit_limit_usd
                ) t;
                """,
                cancellationToken: cancellationToken));

            var unallocated = await connection.QuerySingleAsync<(int Count, decimal Usd)>(new CommandDefinition(
                """
                SELECT COUNT(*)::int AS Count, COALESCE(SUM(unallocated_usd), 0) AS Usd
                FROM payments
                WHERE is_reversed = FALSE AND payment_type = 'RECEIPT' AND unallocated_usd > 0;
                """,
                cancellationToken: cancellationToken));

            var syncFailed = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*)::int FROM erpnext_sync_log WHERE status = 'FAILED';",
                cancellationToken: cancellationToken));

            // Only meaningful once the ERPNext hand-off has produced at least one invoice row.
            var notSynced = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT CASE WHEN EXISTS (SELECT 1 FROM erpnext_sync_log WHERE erpnext_doctype = 'Sales Invoice')
                    THEN (SELECT COUNT(*)::int FROM invoices i
                          WHERE i.status = 'POSTED' AND i.invoice_type = 'SALE'
                            AND NOT EXISTS (SELECT 1 FROM erpnext_sync_log l
                                            WHERE l.local_entity_type = 'Invoice' AND l.local_entity_id = i.id
                                              AND l.erpnext_doctype = 'Sales Invoice' AND l.status = 'SYNCED'))
                    ELSE 0 END;
                """,
                cancellationToken: cancellationToken));

            var summary =
                $"overdue_invoices={overdue.Count} (${overdue.Usd:0.##}); customers_over_credit_limit={overLimit}; " +
                $"unallocated_receipts={unallocated.Count} (${unallocated.Usd:0.##}); erpnext_sync_failed={syncFailed}; " +
                $"posted_invoices_not_synced={notSynced}";

            await WriteRunAsync(connection, "COMPLETED", startedAt, summary, null, cancellationToken);
            _logger.LogInformation("AccountingCheckJob finished. {Summary}", summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AccountingCheckJob failed.");
            await WriteRunAsync(connection, "FAILED", startedAt, null, ex.Message, cancellationToken);
            throw;
        }
    }

    private static Task WriteRunAsync(
        DbConnection connection,
        string status,
        DateTimeOffset startedAt,
        string? summary,
        string? error,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ai_task_runs (id, task_code, status, started_at, completed_at, output_summary, error_message, created_at)
            VALUES (uuid_generate_v4(), @TaskCode, @Status, @StartedAt, now(), @Summary, @Error, now());
            """,
            new { TaskCode, Status = status, StartedAt = startedAt, Summary = summary, Error = error },
            cancellationToken: cancellationToken));
}

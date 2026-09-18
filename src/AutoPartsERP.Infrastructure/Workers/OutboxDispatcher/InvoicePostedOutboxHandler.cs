namespace AutoPartsERP.Infrastructure.Workers.OutboxDispatcher;

public sealed class InvoicePostedOutboxHandler : IOutboxEventHandler
{
    private readonly ILogger<InvoicePostedOutboxHandler> _logger;
    private readonly ErpMetrics _metrics;
    private readonly IErpNextClient _erpNextClient;
    private readonly IDbConnectionFactory _connectionFactory;

    public InvoicePostedOutboxHandler(
        ILogger<InvoicePostedOutboxHandler> logger,
        ErpMetrics metrics,
        IErpNextClient erpNextClient,
        IDbConnectionFactory connectionFactory)
    {
        _logger = logger;
        _metrics = metrics;
        _erpNextClient = erpNextClient;
        _connectionFactory = connectionFactory;
    }

    public string EventType => OutboxEventTypes.InvoicePosted;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<InvoicePostedPayload>(message.PayloadJson);
        if (payload is null)
        {
            return;
        }

        _metrics.RecordInvoicePosted(payload.TotalSyp);
        _logger.LogInformation(
            "Outbox InvoicePosted handled. InvoiceId={InvoiceId} CorrelationId={CorrelationId}",
            payload.InvoiceId,
            message.CorrelationId);

        // Accounting hand-off (see IErpNextClient): NullErpNextClient no-ops until ERPNext is
        // deployed, so this is inert today. The real implementation must fetch invoice_number and
        // invoice_lines via _connectionFactory before calling ERPNext - the outbox payload only
        // carries what InvoicePostedOutboxHandler's other consumers (metrics/logging) need.
        var syncResult = await _erpNextClient.SyncSalesInvoiceAsync(
            new ErpNextSalesInvoiceSync(payload.InvoiceId, string.Empty, payload.CustomerId, payload.InvoiceDate, payload.TotalSyp, payload.TotalUsd, Array.Empty<ErpNextInvoiceLineSync>()),
            cancellationToken);

        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO erpnext_sync_log (id, local_entity_type, local_entity_id, erpnext_doctype, erpnext_name, status, last_error, attempt_count, synced_at, created_at, updated_at)
            VALUES (uuid_generate_v4(), 'Invoice', @InvoiceId, 'Sales Invoice', @ErpNextName, @Status, @LastError, 1, @SyncedAt, now(), now())
            ON CONFLICT (local_entity_type, local_entity_id, erpnext_doctype) DO UPDATE
                SET erpnext_name = EXCLUDED.erpnext_name,
                    status = EXCLUDED.status,
                    last_error = EXCLUDED.last_error,
                    attempt_count = erpnext_sync_log.attempt_count + 1,
                    synced_at = EXCLUDED.synced_at,
                    updated_at = now();
            """,
            new
            {
                payload.InvoiceId,
                ErpNextName = syncResult.IsSuccess ? syncResult.Value : null,
                Status = _erpNextClient.IsEnabled ? (syncResult.IsSuccess ? "SYNCED" : "FAILED") : "SKIPPED",
                LastError = syncResult.IsFailure ? syncResult.Error.Message : null,
                SyncedAt = syncResult.IsSuccess ? DateTimeOffset.UtcNow : (DateTimeOffset?)null
            },
            cancellationToken: cancellationToken));
    }
}

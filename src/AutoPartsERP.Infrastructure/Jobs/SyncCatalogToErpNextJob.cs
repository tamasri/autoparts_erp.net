using AutoPartsERP.Infrastructure.Services;

namespace AutoPartsERP.Infrastructure.Jobs;

/// <summary>
/// Pushes every active sku and party to ERPNext as Items/Customers/Suppliers - the foundation
/// decided first for the accounting hand-off, since Sales Invoice/Payment Entry sync depends on
/// the customer and item already existing there. A no-op while IErpNextClient.IsEnabled is false
/// (NullErpNextClient). Triggerable on demand from the Hangfire dashboard (/hangfire, already
/// SYSTEM_ADMIN-gated) in addition to its recurring schedule, so a first sync doesn't require
/// waiting for the cron tick.
/// </summary>
public sealed class SyncCatalogToErpNextJob
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IErpNextClient _erpNextClient;
    private readonly SalesInvoiceErpNextSyncer _invoiceSyncer;
    private readonly PaymentErpNextSyncer _paymentSyncer;
    private readonly ILogger<SyncCatalogToErpNextJob> _logger;

    public SyncCatalogToErpNextJob(IDbConnectionFactory connectionFactory, IErpNextClient erpNextClient, SalesInvoiceErpNextSyncer invoiceSyncer, PaymentErpNextSyncer paymentSyncer, ILogger<SyncCatalogToErpNextJob> logger)
    {
        _connectionFactory = connectionFactory;
        _erpNextClient = erpNextClient;
        _invoiceSyncer = invoiceSyncer;
        _paymentSyncer = paymentSyncer;
        _logger = logger;
    }

    [Queue("governance")]
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_erpNextClient.IsEnabled)
        {
            _logger.LogDebug("ERPNext catalog sync skipped: ERPNext is not enabled.");
            return;
        }

        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);

        var skus = await connection.QueryAsync<(Guid Id, string Code, string Name, string NameAr, decimal CostPriceUsd, decimal SellingPriceUsd)>(
            new CommandDefinition(
                "SELECT id AS Id, code AS Code, name AS Name, name_ar AS NameAr, cost_price_usd AS CostPriceUsd, selling_price_usd AS SellingPriceUsd FROM skus WHERE is_active = TRUE;",
                cancellationToken: cancellationToken));

        foreach (var sku in skus)
        {
            var result = await _erpNextClient.SyncItemAsync(
                new ErpNextItemSync(sku.Id, sku.Code, sku.Name, sku.NameAr, sku.CostPriceUsd, sku.SellingPriceUsd),
                cancellationToken);

            await LogSyncAsync(connection, "Sku", sku.Id, "Item", result, cancellationToken);
        }

        var parties = await connection.QueryAsync<(Guid Id, string DisplayName, string? TaxNumber)>(
            new CommandDefinition(
                """
                SELECT DISTINCT p.id AS Id, p.display_name AS DisplayName, p.tax_number AS TaxNumber
                FROM parties p
                INNER JOIN party_type_assignments pta ON pta.party_id = p.id
                WHERE p.is_active = TRUE AND pta.is_active = TRUE
                  AND pta.type_code IN ('CUSTOMER', 'VENDOR');
                """,
                cancellationToken: cancellationToken));

        var partyTypes = await connection.QueryAsync<(Guid PartyId, string TypeCode)>(
            new CommandDefinition(
                "SELECT party_id AS PartyId, type_code AS TypeCode FROM party_type_assignments WHERE is_active = TRUE;",
                cancellationToken: cancellationToken));
        var typesByParty = partyTypes.ToLookup(x => x.PartyId, x => x.TypeCode);

        foreach (var party in parties)
        {
            // A party can hold both Customer and Vendor roles; sync each role ERPNext models separately.
            foreach (var typeCode in typesByParty[party.Id].Distinct())
            {
                if (typeCode is not (PartyTypeCodes.Customer or PartyTypeCodes.Vendor))
                {
                    continue;
                }

                var result = await _erpNextClient.SyncPartyAsync(
                    new ErpNextPartySync(party.Id, party.DisplayName, typeCode, party.TaxNumber),
                    cancellationToken);

                await LogSyncAsync(connection, "Party", party.Id, typeCode == PartyTypeCodes.Customer ? "Customer" : "Supplier", result, cancellationToken);
            }
        }

        // Invoices last: they need the customers and items above to exist in ERPNext. Covers the
        // backlog of already-posted invoices and retries any previously FAILED sync.
        var pendingInvoiceIds = await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT i.id
            FROM invoices i
            LEFT JOIN erpnext_sync_log l ON l.local_entity_type = 'Invoice' AND l.local_entity_id = i.id AND l.erpnext_doctype = 'Sales Invoice'
            WHERE i.status = 'POSTED' AND i.invoice_type = 'SALE' AND (l.id IS NULL OR l.status <> 'SYNCED')
            ORDER BY i.invoice_date, i.created_at;
            """,
            cancellationToken: cancellationToken));

        foreach (var invoiceId in pendingInvoiceIds)
        {
            await _invoiceSyncer.SyncAsync(invoiceId, cancellationToken);
        }

        // Receipts last: a Payment Entry references Sales Invoices that must already be in ERPNext.
        foreach (var paymentId in await _paymentSyncer.FindPendingAsync(cancellationToken))
        {
            await _paymentSyncer.SyncAsync(paymentId, cancellationToken);
        }
    }

    private static async Task LogSyncAsync(DbConnection connection, string localEntityType, Guid localEntityId, string doctype, Result<string> result, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO erpnext_sync_log (id, local_entity_type, local_entity_id, erpnext_doctype, erpnext_name, status, last_error, attempt_count, synced_at, created_at, updated_at)
            VALUES (uuid_generate_v4(), @LocalEntityType, @LocalEntityId, @Doctype, @ErpNextName, @Status, @LastError, 1, @SyncedAt, now(), now())
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
                LocalEntityType = localEntityType,
                LocalEntityId = localEntityId,
                Doctype = doctype,
                ErpNextName = result.IsSuccess ? result.Value : null,
                Status = result.IsSuccess ? "SYNCED" : "FAILED",
                LastError = result.IsFailure ? result.Error.Message : null,
                SyncedAt = result.IsSuccess ? DateTimeOffset.UtcNow : (DateTimeOffset?)null
            },
            cancellationToken: cancellationToken));
    }
}

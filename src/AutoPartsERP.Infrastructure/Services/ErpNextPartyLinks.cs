namespace AutoPartsERP.Infrastructure.Services;

/// <summary>
/// The one way every hand-off (catalog job, sales invoices, receipts, supplier bills and payments, journal entries) links a local
/// customer/supplier to ERPNext. It reads the party, the record it is already linked to (whatever the last sync's outcome — a
/// FAILED retry must not create a second record) and the records other parties already own, lets the client link, and records the
/// outcome. Callers must refer to the party in ERPNext by the returned name only.
/// </summary>
internal static class ErpNextPartyLinks
{
    private sealed record PartyRow(string DisplayName, string Code, string? TaxNumber);

    public static async Task<Result<string>> EnsureAsync(
        DbConnection connection, IErpNextClient client, Guid partyId, string typeCode, CancellationToken cancellationToken)
    {
        var doctype = typeCode == PartyTypeCodes.Customer ? "Customer" : "Supplier";
        var party = await connection.QuerySingleOrDefaultAsync<PartyRow>(new CommandDefinition(
            """
            SELECT COALESCE(NULLIF(display_name, ''), display_name_ar) AS DisplayName, code AS Code, tax_number AS TaxNumber
            FROM parties WHERE id = @partyId;
            """,
            new { partyId }, cancellationToken: cancellationToken));
        if (party is null)
        {
            return Result<string>.Failure(new Error("ErpNext.PartyNotFound", $"Party {partyId} does not exist."));
        }

        var known = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT erpnext_name FROM erpnext_sync_log
            WHERE local_entity_type = 'Party' AND local_entity_id = @partyId AND erpnext_doctype = @doctype AND erpnext_name IS NOT NULL;
            """,
            new { partyId, doctype }, cancellationToken: cancellationToken));

        // Every record already linked to ANOTHER party. Not filtered by name: ERPNext may name records by series (CUST-00001), so a
        // record's name says nothing about whose it is. One short string per party — small even for thousands of parties.
        var taken = (await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT erpnext_name FROM erpnext_sync_log
            WHERE local_entity_type = 'Party' AND erpnext_doctype = @doctype AND local_entity_id <> @partyId AND erpnext_name IS NOT NULL;
            """,
            new { partyId, doctype }, cancellationToken: cancellationToken))).ToHashSet(StringComparer.Ordinal);

        var result = await client.SyncPartyAsync(
            new ErpNextPartySync(partyId, party.DisplayName, typeCode, party.TaxNumber, party.Code, known, taken), cancellationToken);

        await ErpNextSyncLogWriter.WriteAsync(
            connection, "Party", partyId, doctype, result.IsSuccess ? result.Value : null,
            client.IsEnabled ? (result.IsSuccess ? ErpNextSyncLogWriter.Synced : ErpNextSyncLogWriter.Failed) : ErpNextSyncLogWriter.Skipped,
            result.IsFailure ? result.Error.Message : null, cancellationToken);

        if (result.IsFailure)
        {
            return Result<string>.Failure(new Error("ErpNext.PartySync", $"{(doctype == "Customer" ? "Customer" : "Supplier")} '{party.DisplayName}' ({party.Code}) could not be linked in ERPNext: {result.Error.Message}"));
        }

        // With ERPNext switched off the client returns no name; documents are skipped in that case anyway.
        return Result<string>.Success(string.IsNullOrEmpty(result.Value) ? party.DisplayName : result.Value);
    }
}

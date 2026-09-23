namespace AutoPartsERP.Infrastructure.Services;

/// <summary>
/// Hands a posted manual accounting entry to ERPNext as a submitted Journal Entry and cancels it there when the entry is voided.
/// A line that names a party (customer or supplier) is sent with that party, created in ERPNext first if it is not there yet;
/// which kind of party it is follows from the account (receivable = customer, payable = supplier).
/// </summary>
public sealed class JournalEntryErpNextSyncer
{
    private const string Entity = "JournalEntry";
    private const string Doctype = "Journal Entry";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IErpNextClient _erpNextClient;

    public JournalEntryErpNextSyncer(IDbConnectionFactory connectionFactory, IErpNextClient erpNextClient)
    {
        _connectionFactory = connectionFactory;
        _erpNextClient = erpNextClient;
    }

    private string StatusOf(bool succeeded) =>
        _erpNextClient.IsEnabled ? (succeeded ? ErpNextSyncLogWriter.Synced : ErpNextSyncLogWriter.Failed) : ErpNextSyncLogWriter.Skipped;

    public async Task SyncAsync(Guid entryId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        if (await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, Entity, entryId, Doctype, cancellationToken) is not null)
        {
            return;
        }

        // Only a posted entry is booked: one voided before it reached ERPNext must never appear there.
        var entry = await connection.QuerySingleOrDefaultAsync<EntryRow>(new CommandDefinition(
            """
            SELECT e.entry_number AS EntryNumber, e.entry_date AS EntryDate, e.narration AS Narration, e.reference_number AS ReferenceNumber, t.kind AS Kind
            FROM journal_entries e INNER JOIN entry_types t ON t.id = e.entry_type_id
            WHERE e.id = @entryId AND e.status = 'POSTED';
            """,
            new { entryId }, cancellationToken: cancellationToken));
        if (entry is null)
        {
            return;
        }

        var lines = (await connection.QueryAsync<LineRow>(new CommandDefinition(
            """
            SELECT l.account_name AS Account, l.party_id AS PartyId, COALESCE(NULLIF(p.display_name, ''), p.display_name_ar) AS PartyName, p.tax_number AS TaxNumber,
                   l.debit_usd AS Debit, l.credit_usd AS Credit, l.narration AS Narration
            FROM journal_entry_lines l LEFT JOIN parties p ON p.id = l.party_id
            WHERE l.journal_entry_id = @entryId ORDER BY l.line_number;
            """,
            new { entryId }, cancellationToken: cancellationToken))).ToList();

        var resolved = await ResolveLinesAsync(connection, lines, cancellationToken);
        if (resolved.IsFailure)
        {
            await ErpNextSyncLogWriter.WriteAsync(connection, Entity, entryId, Doctype, null, StatusOf(false), resolved.Error.Message, cancellationToken);
            return;
        }

        var result = await _erpNextClient.SyncJournalEntryAsync(
            new ErpNextJournalEntrySync(entryId, EntryKinds.ErpNextVoucherType(entry.Kind), entry.EntryNumber, entry.EntryDate, entry.Narration, entry.ReferenceNumber,
                entry.Kind == EntryKinds.Opening, resolved.Value!),
            cancellationToken);
        await ErpNextSyncLogWriter.WriteAsync(connection, Entity, entryId, Doctype, result.IsSuccess ? result.Value : null,
            StatusOf(result.IsSuccess), result.IsFailure ? result.Error.Message : null, cancellationToken);
    }

    /// <summary>Cancels the Journal Entry in ERPNext when the entry is voided; an entry that never got there needs nothing.</summary>
    public async Task CancelAsync(Guid entryId, CancellationToken cancellationToken)
    {
        if (!_erpNextClient.IsEnabled)
        {
            return;
        }

        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        var name = await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, Entity, entryId, Doctype, cancellationToken);
        if (name is null)
        {
            return;
        }

        var result = await _erpNextClient.CancelDocumentAsync(Doctype, name, cancellationToken);
        await ErpNextSyncLogWriter.WriteAsync(connection, Entity, entryId, Doctype, name,
            result.IsSuccess ? ErpNextSyncLogWriter.Cancelled : ErpNextSyncLogWriter.Failed,
            result.IsFailure ? $"Cancel failed: {result.Error.Message}" : null, cancellationToken);
    }

    /// <summary>Posted entries ERPNext does not have yet (or whose hand-off failed).</summary>
    public async Task<IReadOnlyList<Guid>> FindPendingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        return (await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT e.id FROM journal_entries e
            WHERE e.status = 'POSTED'
              AND NOT EXISTS (SELECT 1 FROM erpnext_sync_log l WHERE l.local_entity_type = 'JournalEntry' AND l.local_entity_id = e.id
                                AND l.erpnext_doctype = 'Journal Entry' AND l.status IN ('SYNCED', 'SKIPPED', 'CANCELLED'))
            ORDER BY e.entry_date, e.created_at;
            """,
            cancellationToken: cancellationToken))).ToList();
    }

    private async Task<Result<IReadOnlyList<ErpNextJournalLine>>> ResolveLinesAsync(DbConnection connection, IReadOnlyList<LineRow> lines, CancellationToken cancellationToken)
    {
        Dictionary<string, string?> accountTypes = [];
        if (lines.Any(l => l.PartyId is not null))
        {
            var chart = await _erpNextClient.GetChartOfAccountsAsync(cancellationToken);
            if (chart.IsFailure)
            {
                return Result<IReadOnlyList<ErpNextJournalLine>>.Failure(chart.Error);
            }

            accountTypes = chart.Value!.ToDictionary(a => a.Name, a => a.AccountType, StringComparer.Ordinal);
        }

        var result = new List<ErpNextJournalLine>(lines.Count);
        foreach (var line in lines)
        {
            if (line.PartyId is null || line.PartyName is null)
            {
                result.Add(new ErpNextJournalLine(line.Account, null, null, line.Debit, line.Credit, line.Narration));
                continue;
            }

            var accountType = accountTypes.GetValueOrDefault(line.Account);
            var (erpType, partyType, syncedDoctype) = accountType switch
            {
                "Receivable" => ("Customer", PartyTypeCodes.Customer, "Customer"),
                "Payable" => ("Supplier", PartyTypeCodes.Vendor, "Supplier"),
                _ => (null, null, null)
            };
            if (erpType is null)
            {
                return Result<IReadOnlyList<ErpNextJournalLine>>.Failure(new Error("ErpNext.PartyAccount", $"A party can only be used on a receivable or payable account; '{line.Account}' is neither."));
            }

            var party = await ErpNextPartyLinks.EnsureAsync(connection, _erpNextClient, line.PartyId.Value, partyType!, cancellationToken);
            if (party.IsFailure)
            {
                return Result<IReadOnlyList<ErpNextJournalLine>>.Failure(party.Error);
            }

            result.Add(new ErpNextJournalLine(line.Account, erpType, party.Value!, line.Debit, line.Credit, line.Narration));
        }

        return Result<IReadOnlyList<ErpNextJournalLine>>.Success(result);
    }

    private sealed record EntryRow(string EntryNumber, DateOnly EntryDate, string? Narration, string? ReferenceNumber, string Kind);

    private sealed record LineRow(string Account, Guid? PartyId, string? PartyName, string? TaxNumber, decimal Debit, decimal Credit, string? Narration);
}

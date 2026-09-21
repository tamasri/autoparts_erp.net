using AutoPartsERP.Application.Common.Abstractions.Markers;
using AutoPartsERP.Application.Common.Messaging;
using AutoPartsERP.Contracts.Accounting;

namespace AutoPartsERP.Application.Features.Accounting;

// Manual accounting entries: a draft is prepared and can be edited or deleted; posting makes it final and hands it to ERPNext as a
// Journal Entry; a posted entry is never edited, only voided (which cancels it in ERPNext).

internal static class EntryLineRules
{
    public const string Module = "ACCOUNTING";
    private const decimal Tolerance = 0.0001m;

    public static bool IsBalanced(IReadOnlyList<JournalLineInput> lines) => Math.Abs(lines.Sum(l => l.Debit) - lines.Sum(l => l.Credit)) < Tolerance;

    /// <summary>
    /// Checks the accounts against ERPNext's chart (they must exist and be ledger accounts; a party goes only on a receivable or payable account).
    /// While ERPNext is switched off the chart cannot be read, so the check is skipped and left to the hand-off.
    /// </summary>
    public static async Task<Result> CheckAccountsAsync(IErpNextClient erpNext, IEnumerable<(string Account, bool HasParty)> lines, CancellationToken cancellationToken)
    {
        if (!erpNext.IsEnabled)
        {
            return Result.Success();
        }

        var chart = await erpNext.GetChartOfAccountsAsync(cancellationToken);
        if (chart.IsFailure)
        {
            return Result.Failure(chart.Error);
        }

        var byName = chart.Value!.ToDictionary(a => a.Name, StringComparer.Ordinal);
        foreach (var (account, hasParty) in lines)
        {
            if (!byName.TryGetValue(account, out var found))
            {
                return Result.Failure(new Error("Accounting.AccountNotFound", $"Account '{account}' is not in the chart of accounts."));
            }

            if (found.IsGroup)
            {
                return Result.Failure(new Error("Accounting.NotLedger", $"'{found.AccountName}' is a group; post to one of its accounts."));
            }

            if (hasParty && found.AccountType is not ("Receivable" or "Payable"))
            {
                return Result.Failure(new Error("Accounting.PartyAccount", $"A party can only be used on a receivable or payable account; '{found.AccountName}' is neither."));
            }

            if (!hasParty && found.AccountType is "Receivable" or "Payable")
            {
                return Result.Failure(new Error("Accounting.PartyRequired", $"'{found.AccountName}' needs a party (customer or supplier) on the line."));
            }
        }

        return Result.Success();
    }
}

// ------------------------------------------------------------------ save (create or edit a draft)

public sealed record SaveJournalEntryCommand(Guid? Id, SaveJournalEntryRequest Request)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest, IPeriodSensitiveRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.PostEntries;
    public string AuditModule => "ACCOUNTING";
    public DateTimeOffset OperationDate => Request.EntryDate.ToDateTime(TimeOnly.MinValue);
    public string Module => EntryLineRules.Module;
}

public sealed class SaveJournalEntryCommandValidator : AbstractValidator<SaveJournalEntryCommand>
{
    public SaveJournalEntryCommandValidator()
    {
        RuleFor(x => x.Request.EntryTypeId).NotEmpty();
        RuleFor(x => x.Request.Narration).MaximumLength(500);
        RuleFor(x => x.Request.ReferenceNumber).MaximumLength(60);
        RuleFor(x => x.Request.Lines).NotNull().Must(l => l is { Count: >= 2 and <= 200 }).WithMessage("An entry needs 2 to 200 lines.");
        RuleForEach(x => x.Request.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Account).NotEmpty();
            line.RuleFor(l => l.Debit).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.Credit).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l).Must(l => (l.Debit > 0) != (l.Credit > 0)).WithMessage("Each line is either a debit or a credit, with an amount.");
            line.RuleFor(l => l.Narration).MaximumLength(300);
        }).When(x => x.Request.Lines is not null);
        RuleFor(x => x.Request.Lines).Must(EntryLineRules.IsBalanced).WithMessage("Total debits must equal total credits.")
            .When(x => x.Request.Lines is { Count: > 0 });
    }
}

public sealed class SaveJournalEntryCommandHandler : IRequestHandler<SaveJournalEntryCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IErpNextClient _erpNext;

    public SaveJournalEntryCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, IErpNextClient erpNext)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _erpNext = erpNext;
    }

    public async Task<Result<Guid>> Handle(SaveJournalEntryCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var type = await connection.QuerySingleOrDefaultAsync<(string Kind, string Prefix, bool IsActive)>(new CommandDefinition(
            "SELECT kind AS Kind, prefix AS Prefix, is_active AS IsActive FROM entry_types WHERE id = @EntryTypeId;", new { request.EntryTypeId }, cancellationToken: cancellationToken));
        if (type.Kind is null || !type.IsActive)
        {
            return Result<Guid>.Failure(new Error("EntryType.NotFound", "The entry type was not found or is switched off."));
        }

        if (type.Kind == EntryKinds.Contra && request.Lines.Any(l => l.PartyId is not null))
        {
            return Result<Guid>.Failure(new Error("Accounting.ContraNoParty", "A contra entry moves money between cash and bank accounts and takes no party."));
        }

        var partyIds = request.Lines.Where(l => l.PartyId is not null).Select(l => l.PartyId!.Value).Distinct().ToArray();
        if (partyIds.Length > 0)
        {
            var found = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT count(*) FROM parties WHERE id = ANY(@partyIds) AND is_active;", new { partyIds }, cancellationToken: cancellationToken));
            if (found != partyIds.Length)
            {
                return Result<Guid>.Failure(new Error("Accounting.PartyNotFound", "A party on the entry was not found or is inactive."));
            }
        }

        var accounts = await EntryLineRules.CheckAccountsAsync(_erpNext, request.Lines.Select(l => (l.Account, l.PartyId is not null)), cancellationToken);
        if (accounts.IsFailure)
        {
            return Result<Guid>.Failure(accounts.Error);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var id = command.Id ?? Guid.NewGuid();
        var total = request.Lines.Sum(l => l.Debit);

        if (command.Id is null)
        {
            var number = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                """
                UPDATE entry_types SET last_number = last_number + 1 WHERE id = @EntryTypeId
                RETURNING prefix || '-' || to_char(@EntryDate, 'YYYY') || '-' || lpad(last_number::text, 5, '0');
                """,
                new { request.EntryTypeId, request.EntryDate }, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO journal_entries (id, entry_number, entry_type_id, entry_date, narration, reference_number, total_usd, created_by)
                VALUES (@id, @number, @EntryTypeId, @EntryDate, @Narration, @ReferenceNumber, @total, @By);
                """,
                new { id, number, request.EntryTypeId, request.EntryDate, request.Narration, request.ReferenceNumber, total, By = _currentUser.UserId }, transaction, cancellationToken: cancellationToken));
        }
        else
        {
            var status = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT status FROM journal_entries WHERE id = @id FOR UPDATE;", new { id }, transaction, cancellationToken: cancellationToken));
            if (status is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("JournalEntry.NotFound", "Entry was not found."));
            }

            if (status != "DRAFT")
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("JournalEntry.NotDraft", "Only a draft can be edited; void a posted entry and enter it again."));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE journal_entries
                SET entry_type_id = @EntryTypeId, entry_date = @EntryDate, narration = @Narration, reference_number = @ReferenceNumber, total_usd = @total, updated_at = now()
                WHERE id = @id;
                DELETE FROM journal_entry_lines WHERE journal_entry_id = @id;
                """,
                new { id, request.EntryTypeId, request.EntryDate, request.Narration, request.ReferenceNumber, total }, transaction, cancellationToken: cancellationToken));
        }

        var lineNumber = 0;
        foreach (var line in request.Lines)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO journal_entry_lines (journal_entry_id, line_number, account_name, party_id, debit_usd, credit_usd, narration)
                VALUES (@id, @n, @Account, @PartyId, @Debit, @Credit, @Narration);
                """,
                new { id, n = ++lineNumber, line.Account, line.PartyId, line.Debit, line.Credit, line.Narration }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(id);
    }
}

// ------------------------------------------------------------------ post / void / delete

/// <summary>Posting is final, so it needs a second person's approval (SYSTEM_ADMIN is exempt). The period lock is checked by the handler against the entry's own date.</summary>
public sealed record PostJournalEntryCommand(Guid Id) : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.PostEntries;
    public string AuditModule => "ACCOUNTING";
    public bool RequiresApproval => true;
}

public sealed class PostJournalEntryCommandHandler : IRequestHandler<PostJournalEntryCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IPeriodLockService _periodLock;
    private readonly IErpNextClient _erpNext;

    public PostJournalEntryCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, IPeriodLockService periodLock, IErpNextClient erpNext)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _periodLock = periodLock;
        _erpNext = erpNext;
    }

    public async Task<Result<Guid>> Handle(PostJournalEntryCommand command, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var entry = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Status, DateOnly EntryDate)>(new CommandDefinition(
            "SELECT id AS Id, status AS Status, entry_date AS EntryDate FROM journal_entries WHERE id = @Id FOR UPDATE;", new { command.Id }, transaction, cancellationToken: cancellationToken));
        if (entry.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("JournalEntry.NotFound", "Entry was not found."));
        }

        if (entry.Status != "DRAFT")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("JournalEntry.NotDraft", "Only a draft can be posted."));
        }

        if (await _periodLock.IsLockedAsync(entry.EntryDate.Year, entry.EntryDate.Month, EntryLineRules.Module, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("PeriodLock.Locked", $"Period {entry.EntryDate:yyyy-MM} is locked for accounting entries."));
        }

        var lines = (await connection.QueryAsync<(string Account, Guid? PartyId)>(new CommandDefinition(
            "SELECT account_name AS Account, party_id AS PartyId FROM journal_entry_lines WHERE journal_entry_id = @Id;", new { command.Id }, transaction, cancellationToken: cancellationToken))).ToList();
        var accounts = await EntryLineRules.CheckAccountsAsync(_erpNext, lines.Select(l => (l.Account, l.PartyId is not null)), cancellationToken);
        if (accounts.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(accounts.Error);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE journal_entries SET status = 'POSTED', posted_by = @By, posted_at = now(), updated_at = now() WHERE id = @Id;",
            new { command.Id, By = _currentUser.UserId }, transaction, cancellationToken: cancellationToken));
        await OutboxWriter.AddAsync(connection, transaction, OutboxEventTypes.JournalEntryPosted, "JournalEntry", command.Id,
            new JournalEntryEventPayload(command.Id), _currentUser.CorrelationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(command.Id);
    }
}

/// <summary>Voiding removes a booked entry from the ledger, so it needs approval like posting does.</summary>
public sealed record VoidJournalEntryCommand(Guid Id, string Reason) : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.PostEntries;
    public string AuditModule => "ACCOUNTING";
    public bool RequiresApproval => true;
}

public sealed class VoidJournalEntryCommandValidator : AbstractValidator<VoidJournalEntryCommand>
{
    public VoidJournalEntryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(5);
    }
}

public sealed class VoidJournalEntryCommandHandler : IRequestHandler<VoidJournalEntryCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IPeriodLockService _periodLock;

    public VoidJournalEntryCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, IPeriodLockService periodLock)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _periodLock = periodLock;
    }

    public async Task<Result<Guid>> Handle(VoidJournalEntryCommand command, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var entry = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Status, DateOnly EntryDate)>(new CommandDefinition(
            "SELECT id AS Id, status AS Status, entry_date AS EntryDate FROM journal_entries WHERE id = @Id FOR UPDATE;", new { command.Id }, transaction, cancellationToken: cancellationToken));
        if (entry.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("JournalEntry.NotFound", "Entry was not found."));
        }

        if (entry.Status != "POSTED")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("JournalEntry.NotPosted", "Only a posted entry can be voided."));
        }

        if (await _periodLock.IsLockedAsync(entry.EntryDate.Year, entry.EntryDate.Month, EntryLineRules.Module, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("PeriodLock.Locked", $"Period {entry.EntryDate:yyyy-MM} is locked for accounting entries."));
        }

        // A line that was reconciled against a statement must be un-reconciled first, or the reconciliation would silently stop adding up.
        var reconciled = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (
                SELECT 1 FROM ledger_reconciliation_items i
                INNER JOIN erpnext_sync_log s ON s.erpnext_name = i.voucher_no AND s.erpnext_doctype = 'Journal Entry'
                WHERE i.voucher_type = 'Journal Entry' AND s.local_entity_type = 'JournalEntry' AND s.local_entity_id = @Id);
            """,
            new { command.Id }, transaction, cancellationToken: cancellationToken));
        if (reconciled)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Accounting.EntryReconciled", "A line of this entry is already reconciled; undo that reconciliation first."));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE journal_entries SET status = 'VOID', voided_by = @By, voided_at = now(), void_reason = @Reason, updated_at = now() WHERE id = @Id;",
            new { command.Id, command.Reason, By = _currentUser.UserId }, transaction, cancellationToken: cancellationToken));
        await OutboxWriter.AddAsync(connection, transaction, OutboxEventTypes.JournalEntryVoided, "JournalEntry", command.Id,
            new JournalEntryEventPayload(command.Id), _currentUser.CorrelationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(command.Id);
    }
}

public sealed record DeleteJournalEntryCommand(Guid Id) : IRequest<Result>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.PostEntries;
    public string AuditModule => "ACCOUNTING";
}

public sealed class DeleteJournalEntryCommandHandler : IRequestHandler<DeleteJournalEntryCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DeleteJournalEntryCommandHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result> Handle(DeleteJournalEntryCommand command, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var status = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT status FROM journal_entries WHERE id = @Id FOR UPDATE;", new { command.Id }, transaction, cancellationToken: cancellationToken));
        if (status is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure(new Error("JournalEntry.NotFound", "Entry was not found."));
        }

        if (status != "DRAFT")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure(new Error("JournalEntry.NotDraft", "Only a draft can be deleted; void a posted entry instead."));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM accounting_tag_links WHERE target_type = 'JOURNAL_ENTRY' AND target_key = @Key;
            DELETE FROM journal_entries WHERE id = @Id;
            """,
            new { command.Id, Key = command.Id.ToString() }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}

// ------------------------------------------------------------------ reads

public sealed record GetJournalEntriesQuery(int PageNumber, int PageSize, string? Status, Guid? EntryTypeId, string? Search, DateOnly? From, DateOnly? To, Guid? TagId)
    : IRequest<Result<PagedResponse<JournalEntryListDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetJournalEntriesQueryHandler : IRequestHandler<GetJournalEntriesQuery, Result<PagedResponse<JournalEntryListDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetJournalEntriesQueryHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result<PagedResponse<JournalEntryListDto>>> Handle(GetJournalEntriesQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(request.PageNumber, 1);
        var size = Math.Clamp(request.PageSize, 1, 200);
        var args = new
        {
            Status = string.IsNullOrWhiteSpace(request.Status) ? null : request.Status.Trim().ToUpperInvariant(),
            request.EntryTypeId,
            Search = string.IsNullOrWhiteSpace(request.Search) ? null : $"%{request.Search.Trim()}%",
            request.From,
            request.To,
            request.TagId,
            Offset = (page - 1) * size,
            Size = size
        };
        const string where = """
            WHERE (@Status::text IS NULL OR e.status = @Status)
              AND (@EntryTypeId::uuid IS NULL OR e.entry_type_id = @EntryTypeId)
              AND (@Search::text IS NULL OR e.entry_number ILIKE @Search OR e.narration ILIKE @Search OR e.reference_number ILIKE @Search)
              AND (@From::date IS NULL OR e.entry_date >= @From)
              AND (@To::date IS NULL OR e.entry_date <= @To)
              AND (@TagId::uuid IS NULL OR EXISTS (
                    SELECT 1 FROM accounting_tag_links tl
                    WHERE tl.tag_id = @TagId
                      AND ((tl.target_type = 'JOURNAL_ENTRY' AND tl.target_key = e.id::text)
                        OR (tl.target_type = 'ERPNEXT' AND s.erpnext_name IS NOT NULL AND tl.target_key = 'Journal Entry|' || s.erpnext_name))))
            """;
        const string from = """
            FROM journal_entries e
            INNER JOIN entry_types t ON t.id = e.entry_type_id
            LEFT JOIN erpnext_sync_log s ON s.local_entity_type = 'JournalEntry' AND s.local_entity_id = e.id AND s.erpnext_doctype = 'Journal Entry'
            """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<EntryRow>(new CommandDefinition(
            $"""
            SELECT e.id AS Id, e.entry_number AS EntryNumber, t.code AS TypeCode, t.name_ar AS TypeNameAr, t.kind AS Kind, e.entry_date AS EntryDate,
                   e.status AS Status, e.narration AS Narration, e.total_usd AS TotalUsd, s.erpnext_name AS ErpNextName,
                   COALESCE(s.status, CASE WHEN e.status = 'POSTED' THEN 'PENDING' ELSE 'NONE' END) AS SyncStatus, s.last_error AS SyncError
            {from} {where}
            ORDER BY e.entry_date DESC, e.created_at DESC
            OFFSET @Offset LIMIT @Size;
            """,
            args, cancellationToken: cancellationToken))).ToList();
        var total = await connection.ExecuteScalarAsync<long>(new CommandDefinition($"SELECT count(*) {from} {where};", args, cancellationToken: cancellationToken));

        var tags = await TagResolver.ForEntriesAsync(connection, rows.Select(r => r.Id), cancellationToken);
        return Result<PagedResponse<JournalEntryListDto>>.Success(new PagedResponse<JournalEntryListDto>(rows.Select(r => r.ToDto(tags.GetValueOrDefault(r.Id) ?? [])).ToList(), page, size, total));
    }
}

public sealed record GetJournalEntryQuery(Guid Id) : IRequest<Result<JournalEntryDetailDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetJournalEntryQueryHandler : IRequestHandler<GetJournalEntryQuery, Result<JournalEntryDetailDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetJournalEntryQueryHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result<JournalEntryDetailDto>> Handle(GetJournalEntryQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<DetailRow>(new CommandDefinition(
            """
            SELECT e.id AS Id, e.entry_number AS EntryNumber, t.code AS TypeCode, t.name_ar AS TypeNameAr, t.kind AS Kind, e.entry_date AS EntryDate,
                   e.status AS Status, e.narration AS Narration, e.total_usd AS TotalUsd, s.erpnext_name AS ErpNextName,
                   COALESCE(s.status, CASE WHEN e.status = 'POSTED' THEN 'PENDING' ELSE 'NONE' END) AS SyncStatus, s.last_error AS SyncError,
                   e.reference_number AS ReferenceNumber, e.void_reason AS VoidReason
            FROM journal_entries e
            INNER JOIN entry_types t ON t.id = e.entry_type_id
            LEFT JOIN erpnext_sync_log s ON s.local_entity_type = 'JournalEntry' AND s.local_entity_id = e.id AND s.erpnext_doctype = 'Journal Entry'
            WHERE e.id = @Id;
            """,
            new { request.Id }, cancellationToken: cancellationToken));
        if (row is null)
        {
            return Result<JournalEntryDetailDto>.Failure(new Error("JournalEntry.NotFound", "Entry was not found."));
        }

        var lines = (await connection.QueryAsync<JournalLineDto>(new CommandDefinition(
            """
            SELECT l.line_number AS LineNumber, l.account_name AS Account, l.party_id AS PartyId, COALESCE(NULLIF(p.display_name_ar, ''), p.display_name) AS PartyName,
                   l.debit_usd AS Debit, l.credit_usd AS Credit, l.narration AS Narration
            FROM journal_entry_lines l LEFT JOIN parties p ON p.id = l.party_id
            WHERE l.journal_entry_id = @Id ORDER BY l.line_number;
            """,
            new { request.Id }, cancellationToken: cancellationToken))).ToList();
        var tags = await TagResolver.ForEntriesAsync(connection, [row.Id], cancellationToken);
        return Result<JournalEntryDetailDto>.Success(new JournalEntryDetailDto(row.ToDto(tags.GetValueOrDefault(row.Id) ?? []), row.ReferenceNumber, row.VoidReason, lines));
    }
}

internal sealed record EntryRow(
    Guid Id, string EntryNumber, string TypeCode, string TypeNameAr, string Kind, DateOnly EntryDate, string Status, string? Narration, decimal TotalUsd,
    string? ErpNextName, string SyncStatus, string? SyncError)
{
    public JournalEntryListDto ToDto(IReadOnlyList<TagDto> tags) =>
        new(Id, EntryNumber, TypeCode, TypeNameAr, Kind, EntryDate, Status, Narration, TotalUsd, ErpNextName, SyncStatus, SyncError, tags);
}

internal sealed record DetailRow(
    Guid Id, string EntryNumber, string TypeCode, string TypeNameAr, string Kind, DateOnly EntryDate, string Status, string? Narration, decimal TotalUsd,
    string? ErpNextName, string SyncStatus, string? SyncError, string? ReferenceNumber, string? VoidReason)
{
    public JournalEntryListDto ToDto(IReadOnlyList<TagDto> tags) =>
        new(Id, EntryNumber, TypeCode, TypeNameAr, Kind, EntryDate, Status, Narration, TotalUsd, ErpNextName, SyncStatus, SyncError, tags);
}

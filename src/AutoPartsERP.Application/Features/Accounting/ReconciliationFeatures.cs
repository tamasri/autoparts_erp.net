using AutoPartsERP.Contracts.Accounting;

namespace AutoPartsERP.Application.Features.Accounting;

// Ledger account reconciliation: the ledger lines of one account (typically a bank or cash account) are ticked off against a statement.
// ERPNext supplies the ledger lines; which of them have been reconciled is remembered here, so a line is never cleared twice.

internal static class ReconciliationLedger
{
    public const int MaxRows = 5000;

    public sealed record Snapshot(ErpNextAccount Account, bool DebitNormal, decimal BookBalance, IReadOnlyList<ErpNextGlEntry> Entries, bool Truncated)
    {
        public decimal Sign => DebitNormal ? 1m : -1m;
    }

    /// <summary>The account, its balance per the books and its ledger lines, all up to <paramref name="asOf"/>.</summary>
    public static async Task<Result<Snapshot>> LoadAsync(IErpNextClient erpNext, string accountName, DateOnly asOf, CancellationToken cancellationToken)
    {
        var chart = await erpNext.GetChartOfAccountsAsync(cancellationToken);
        if (chart.IsFailure) return Result<Snapshot>.Failure(chart.Error);
        var account = chart.Value!.FirstOrDefault(a => a.Name == accountName);
        if (account is null) return Result<Snapshot>.Failure(new Error("Accounting.NotFound", "The account was not found."));
        if (account.IsGroup) return Result<Snapshot>.Failure(new Error("Accounting.NotLedger", "Choose a ledger account, not a group."));

        var balances = await erpNext.GetGlBalancesAsync(null, asOf, cancellationToken);
        if (balances.IsFailure) return Result<Snapshot>.Failure(balances.Error);
        var entries = await erpNext.GetGlEntriesAsync(new ErpNextGlFilter(account.Name, null, null, null, asOf, MaxRows), cancellationToken);
        if (entries.IsFailure) return Result<Snapshot>.Failure(entries.Error);

        var debitNormal = ChartTree.IsDebitNormal(account.RootType);
        var book = balances.Value!.Where(b => b.Account == account.Name).Sum(b => b.Debit - b.Credit);
        return Result<Snapshot>.Success(new Snapshot(account, debitNormal, (debitNormal ? 1m : -1m) * book, entries.Value!.Take(MaxRows).ToList(), entries.Value!.Count > MaxRows));
    }

    /// <summary>Debit minus credit of everything already reconciled on the account, and which lines those are.</summary>
    public static async Task<(decimal Signed, HashSet<string> Names)> ReconciledAsync(DbConnection connection, string account, CancellationToken cancellationToken)
    {
        var rows = (await connection.QueryAsync<(string Name, decimal Amount)>(new CommandDefinition(
            "SELECT gl_entry_name AS Name, signed_amount AS Amount FROM ledger_reconciliation_items WHERE account_name = @account;",
            new { account }, cancellationToken: cancellationToken))).ToList();
        return (rows.Sum(r => r.Amount), rows.Select(r => r.Name).ToHashSet(StringComparer.Ordinal));
    }

    public static ReconcileCandidateDto ToCandidate(ErpNextGlEntry e) => new(e.Name, e.PostingDate, e.VoucherType, e.VoucherNo, e.Party, e.Remarks, e.Debit, e.Credit);
}

// ------------------------------------------------------------------ what is left to reconcile

public sealed record GetReconcileCandidatesQuery(string Account, DateOnly AsOf) : IRequest<Result<ReconcileCandidatesDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetReconcileCandidatesQueryHandler : IRequestHandler<GetReconcileCandidatesQuery, Result<ReconcileCandidatesDto>>
{
    private readonly IErpNextClient _erpNext;
    private readonly IDbConnectionFactory _connectionFactory;

    public GetReconcileCandidatesQueryHandler(IErpNextClient erpNext, IDbConnectionFactory connectionFactory)
    {
        _erpNext = erpNext;
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<ReconcileCandidatesDto>> Handle(GetReconcileCandidatesQuery request, CancellationToken cancellationToken)
    {
        var ledger = await ReconciliationLedger.LoadAsync(_erpNext, request.Account, request.AsOf, cancellationToken);
        if (ledger.IsFailure) return Result<ReconcileCandidatesDto>.Failure(ledger.Error);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var (signed, names) = await ReconciliationLedger.ReconciledAsync(connection, request.Account, cancellationToken);
        var snapshot = ledger.Value!;
        return Result<ReconcileCandidatesDto>.Success(new ReconcileCandidatesDto(
            request.Account, request.AsOf, snapshot.DebitNormal, snapshot.BookBalance, snapshot.Sign * signed,
            snapshot.Entries.Where(e => !names.Contains(e.Name)).Select(ReconciliationLedger.ToCandidate).ToList(), snapshot.Truncated));
    }
}

// ------------------------------------------------------------------ complete / undo

/// <summary>
/// Records a reconciliation: the ticked lines, cleared against the statement's closing balance. It only goes through when the
/// statement balance equals what was cleared before plus what is ticked now; the amounts are read from ERPNext, never taken from the caller.
/// </summary>
public sealed record CompleteReconciliationCommand(CompleteReconciliationRequest Request) : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Reconcile;
    public string AuditModule => "ACCOUNTING";
}

public sealed class CompleteReconciliationCommandValidator : AbstractValidator<CompleteReconciliationCommand>
{
    public CompleteReconciliationCommandValidator()
    {
        RuleFor(x => x.Request.Account).NotEmpty();
        RuleFor(x => x.Request.StatementDate).LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))).WithMessage("The statement date is in the future.");
        RuleFor(x => x.Request.GlEntryNames).NotEmpty().WithMessage("Tick at least one line.");
        RuleFor(x => x.Request.GlEntryNames.Count).LessThanOrEqualTo(ReconciliationLedger.MaxRows);
        RuleFor(x => x.Request.Notes).MaximumLength(500);
    }
}

public sealed class CompleteReconciliationCommandHandler : IRequestHandler<CompleteReconciliationCommand, Result<Guid>>
{
    private const decimal Tolerance = 0.005m;

    private readonly IErpNextClient _erpNext;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CompleteReconciliationCommandHandler(IErpNextClient erpNext, IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _erpNext = erpNext;
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(CompleteReconciliationCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var ledger = await ReconciliationLedger.LoadAsync(_erpNext, request.Account, request.StatementDate, cancellationToken);
        if (ledger.IsFailure) return Result<Guid>.Failure(ledger.Error);
        var snapshot = ledger.Value!;

        var wanted = request.GlEntryNames.ToHashSet(StringComparer.Ordinal);
        var selected = snapshot.Entries.Where(e => wanted.Contains(e.Name)).ToList();
        if (selected.Count != wanted.Count)
        {
            return Result<Guid>.Failure(new Error("Reconciliation.EntryNotFound", "A ticked line is not in the ledger up to the statement date (it may be dated after it, or was cancelled)."));
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var (signedBefore, done) = await ReconciliationLedger.ReconciledAsync(connection, request.Account, cancellationToken);
        if (selected.Any(e => done.Contains(e.Name)))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Reconciliation.AlreadyCleared", "A ticked line was already reconciled."));
        }

        var cleared = selected.Sum(e => e.Debit - e.Credit);
        var expected = snapshot.Sign * (signedBefore + cleared);
        var difference = request.StatementBalance - expected;
        if (Math.Abs(difference) >= Tolerance)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Reconciliation.Unbalanced",
                $"The ticked lines do not match the statement: difference {difference.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}."));
        }

        var id = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ledger_reconciliations (id, account_name, statement_date, statement_balance, cleared_total, notes, completed_by)
            VALUES (@id, @Account, @StatementDate, @StatementBalance, @cleared, @Notes, @By);
            """,
            new { id, request.Account, request.StatementDate, request.StatementBalance, cleared = snapshot.Sign * cleared, request.Notes, By = _currentUser.UserId }, transaction, cancellationToken: cancellationToken));
        foreach (var e in selected)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO ledger_reconciliation_items (reconciliation_id, account_name, gl_entry_name, posting_date, voucher_type, voucher_no, signed_amount)
                VALUES (@id, @Account, @Name, @PostingDate, @VoucherType, @VoucherNo, @amount);
                """,
                new { id, request.Account, e.Name, e.PostingDate, e.VoucherType, e.VoucherNo, amount = e.Debit - e.Credit }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(id);
    }
}

/// <summary>Reopens the lines of the account's latest reconciliation. Only the latest can be undone, so the earlier ones keep adding up.</summary>
public sealed record UndoReconciliationCommand(Guid Id) : IRequest<Result>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Reconcile;
    public string AuditModule => "ACCOUNTING";
}

public sealed class UndoReconciliationCommandHandler : IRequestHandler<UndoReconciliationCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public UndoReconciliationCommandHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result> Handle(UndoReconciliationCommand command, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var current = await connection.QuerySingleOrDefaultAsync<(string Account, DateTimeOffset CompletedAt)>(new CommandDefinition(
            "SELECT account_name AS Account, completed_at AS CompletedAt FROM ledger_reconciliations WHERE id = @Id;", new { command.Id }, cancellationToken: cancellationToken));
        if (current.Account is null)
        {
            return Result.Failure(new Error("Reconciliation.NotFound", "Reconciliation was not found."));
        }

        var later = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM ledger_reconciliations WHERE account_name = @Account AND completed_at > @CompletedAt);", new { current.Account, current.CompletedAt }, cancellationToken: cancellationToken));
        if (later)
        {
            return Result.Failure(new Error("Reconciliation.NotLatest", "A later reconciliation exists for this account; undo that one first."));
        }

        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM ledger_reconciliations WHERE id = @Id;", new { command.Id }, cancellationToken: cancellationToken));
        return Result.Success();
    }
}

// ------------------------------------------------------------------ history and the reconciliation statement

public sealed record GetReconciliationsQuery(string? Account) : IRequest<Result<IReadOnlyList<ReconciliationListDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetReconciliationsQueryHandler : IRequestHandler<GetReconciliationsQuery, Result<IReadOnlyList<ReconciliationListDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetReconciliationsQueryHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result<IReadOnlyList<ReconciliationListDto>>> Handle(GetReconciliationsQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ReconciliationListDto>(new CommandDefinition(
            """
            SELECT r.id AS Id, r.account_name AS Account, r.statement_date AS StatementDate, r.statement_balance AS StatementBalance, r.cleared_total AS ClearedTotal,
                   (SELECT count(*)::int FROM ledger_reconciliation_items i WHERE i.reconciliation_id = r.id) AS ItemCount,
                   r.completed_at AS CompletedAt, u.user_name AS CompletedBy
            FROM ledger_reconciliations r LEFT JOIN asp_net_users u ON u.id = r.completed_by
            WHERE (@Account::text IS NULL OR r.account_name = @Account)
            ORDER BY r.completed_at DESC LIMIT 200;
            """,
            new { request.Account }, cancellationToken: cancellationToken));
        return Result<IReadOnlyList<ReconciliationListDto>>.Success(rows.ToList());
    }
}

public sealed record GetReconciliationQuery(Guid Id) : IRequest<Result<ReconciliationDetailDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetReconciliationQueryHandler : IRequestHandler<GetReconciliationQuery, Result<ReconciliationDetailDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetReconciliationQueryHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result<ReconciliationDetailDto>> Handle(GetReconciliationQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var header = await connection.QuerySingleOrDefaultAsync<ReconciliationListDto>(new CommandDefinition(
            """
            SELECT r.id AS Id, r.account_name AS Account, r.statement_date AS StatementDate, r.statement_balance AS StatementBalance, r.cleared_total AS ClearedTotal,
                   (SELECT count(*)::int FROM ledger_reconciliation_items i WHERE i.reconciliation_id = r.id) AS ItemCount,
                   r.completed_at AS CompletedAt, u.user_name AS CompletedBy
            FROM ledger_reconciliations r LEFT JOIN asp_net_users u ON u.id = r.completed_by
            WHERE r.id = @Id;
            """,
            new { request.Id }, cancellationToken: cancellationToken));
        if (header is null)
        {
            return Result<ReconciliationDetailDto>.Failure(new Error("Reconciliation.NotFound", "Reconciliation was not found."));
        }

        var items = (await connection.QueryAsync<ReconciliationItemDto>(new CommandDefinition(
            """
            SELECT gl_entry_name AS GlName, posting_date AS PostingDate, voucher_type AS VoucherType, voucher_no AS VoucherNo, signed_amount AS SignedAmount
            FROM ledger_reconciliation_items WHERE reconciliation_id = @Id ORDER BY posting_date, gl_entry_name;
            """,
            new { request.Id }, cancellationToken: cancellationToken))).ToList();
        return Result<ReconciliationDetailDto>.Success(new ReconciliationDetailDto(header, items));
    }
}

/// <summary>Balance per the books, less or plus the lines that have not cleared, gives the balance per the statement.</summary>
public sealed record GetReconciliationStatementQuery(string Account, DateOnly AsOf) : IRequest<Result<ReconciliationStatementDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetReconciliationStatementQueryHandler : IRequestHandler<GetReconciliationStatementQuery, Result<ReconciliationStatementDto>>
{
    private readonly IErpNextClient _erpNext;
    private readonly IDbConnectionFactory _connectionFactory;

    public GetReconciliationStatementQueryHandler(IErpNextClient erpNext, IDbConnectionFactory connectionFactory)
    {
        _erpNext = erpNext;
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<ReconciliationStatementDto>> Handle(GetReconciliationStatementQuery request, CancellationToken cancellationToken)
    {
        var ledger = await ReconciliationLedger.LoadAsync(_erpNext, request.Account, request.AsOf, cancellationToken);
        if (ledger.IsFailure) return Result<ReconciliationStatementDto>.Failure(ledger.Error);
        var snapshot = ledger.Value!;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var (_, done) = await ReconciliationLedger.ReconciledAsync(connection, request.Account, cancellationToken);
        var uncleared = snapshot.Entries.Where(e => !done.Contains(e.Name)).ToList();
        var last = await connection.ExecuteScalarAsync<DateOnly?>(new CommandDefinition(
            "SELECT max(statement_date) FROM ledger_reconciliations WHERE account_name = @Account AND statement_date <= @AsOf;", new { request.Account, request.AsOf }, cancellationToken: cancellationToken));

        var debits = uncleared.Sum(e => e.Debit);
        var credits = uncleared.Sum(e => e.Credit);
        var unclearedNatural = snapshot.Sign * (debits - credits);
        return Result<ReconciliationStatementDto>.Success(new ReconciliationStatementDto(
            request.Account, request.AsOf, snapshot.DebitNormal, snapshot.BookBalance, debits, credits, snapshot.BookBalance - unclearedNatural,
            uncleared.Select(ReconciliationLedger.ToCandidate).ToList(), last));
    }
}

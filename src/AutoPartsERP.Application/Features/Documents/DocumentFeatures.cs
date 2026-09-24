namespace AutoPartsERP.Application.Features.Documents;

// Numbered documents across the application: stepping through a series by number, the Super Admin's deletion of a draft or a
// voided document (its number stays used, with a full snapshot in deleted_documents), and the numbering check that proves every
// number of every series is either a live document or a recorded deletion.

// ------------------------------------------------------------------ previous / next

public sealed record GetDocumentNeighborsQuery(string Kind, Guid Id) : IRequest<Result<DocumentNeighborsDto>>, IAuthorizedRequest
{
    public string RequiredPermission => NumberedDocuments.Find(Kind)?.ReadPermission ?? NumberedDocuments.NoSuchKind;
}

public sealed class GetDocumentNeighborsQueryValidator : AbstractValidator<GetDocumentNeighborsQuery>
{
    public GetDocumentNeighborsQueryValidator()
    {
        RuleFor(x => x.Kind).Must(k => NumberedDocuments.Find(k) is not null).WithMessage("Unknown kind of document.");
        RuleFor(x => x.Id).NotEmpty();
    }
}

public sealed class GetDocumentNeighborsQueryHandler : IRequestHandler<GetDocumentNeighborsQuery, Result<DocumentNeighborsDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetDocumentNeighborsQueryHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    private sealed record Row(
        string SeriesCode, string SeriesNameAr, long SerialNo, string Number,
        Guid? FirstId, long? FirstSerial, string? FirstNumber, Guid? PrevId, long? PrevSerial, string? PrevNumber,
        Guid? NextId, long? NextSerial, string? NextNumber, Guid? LastId, long? LastSerial, string? LastNumber);

    public async Task<Result<DocumentNeighborsDto>> Handle(GetDocumentNeighborsQuery request, CancellationToken cancellationToken)
    {
        var kind = NumberedDocuments.Find(request.Kind)!;
        string Pick(string alias, string where, string order) =>
            $"LEFT JOIN LATERAL (SELECT x.id, x.serial_no, x.{kind.NumberColumn} AS number FROM {kind.Table} x " +
            $"WHERE x.series_code = c.series_code {where} ORDER BY x.serial_no {order} LIMIT 1) {alias} ON TRUE";

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"""
            SELECT c.series_code AS SeriesCode, s.name_ar AS SeriesNameAr, c.serial_no AS SerialNo, c.{kind.NumberColumn} AS Number,
                   f.id AS FirstId, f.serial_no AS FirstSerial, f.number AS FirstNumber,
                   p.id AS PrevId, p.serial_no AS PrevSerial, p.number AS PrevNumber,
                   n.id AS NextId, n.serial_no AS NextSerial, n.number AS NextNumber,
                   l.id AS LastId, l.serial_no AS LastSerial, l.number AS LastNumber
            FROM {kind.Table} c
            INNER JOIN document_series s ON s.code = c.series_code
            {Pick("f", "", "ASC")}
            {Pick("p", "AND x.serial_no < c.serial_no", "DESC")}
            {Pick("n", "AND x.serial_no > c.serial_no", "ASC")}
            {Pick("l", "", "DESC")}
            WHERE c.id = @Id;
            """,
            new { request.Id }, cancellationToken: cancellationToken));
        if (row is null)
        {
            return Result<DocumentNeighborsDto>.Failure(new Error("Document.NotFound", "The document was not found."));
        }

        static DocumentRefDto? Ref(Guid? id, long? serial, string? number) => id is { } i ? new DocumentRefDto(i, serial!.Value, number!) : null;
        return Result<DocumentNeighborsDto>.Success(new DocumentNeighborsDto(
            row.SeriesCode, row.SeriesNameAr, row.SerialNo, row.Number,
            Ref(row.FirstId, row.FirstSerial, row.FirstNumber), Ref(row.PrevId, row.PrevSerial, row.PrevNumber),
            Ref(row.NextId, row.NextSerial, row.NextNumber), Ref(row.LastId, row.LastSerial, row.LastNumber)));
    }
}

// ------------------------------------------------------------------ delete (Super Admin)

/// <summary>
/// Deletes a draft or a voided invoice, purchase invoice or accounting entry. Posted documents are never deleted: void them first
/// (which reverses their stock and ledger effects and cancels them in ERPNext), as ERPNext requires cancelling before deleting.
/// The number stays used: the series shows a recorded gap, never a reused number.
/// </summary>
public sealed record DeleteDocumentCommand(string Kind, Guid Id, string Reason) : IRequest<Result>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Documents.Delete;
    public string AuditModule => "DOCUMENTS";
}

public sealed class DeleteDocumentCommandValidator : AbstractValidator<DeleteDocumentCommand>
{
    public DeleteDocumentCommandValidator()
    {
        RuleFor(x => x.Kind).Must(k => NumberedDocuments.Find(k)?.Deletion is not null).WithMessage("This kind of document cannot be deleted.");
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().Must(r => r.Trim().Length >= 5).WithMessage("Give the reason for the deletion (at least 5 characters).").MaximumLength(500);
    }
}

public sealed class DeleteDocumentCommandHandler : IRequestHandler<DeleteDocumentCommand, Result>
{
    private static readonly string[] Deletable = ["DRAFT", "CONFIRMED", "VOID"];

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IPeriodLockService _periodLock;

    public DeleteDocumentCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, IPeriodLockService periodLock)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _periodLock = periodLock;
    }

    private sealed record Target(Guid Id, string Status, DateOnly Date, string Number);

    public async Task<Result> Handle(DeleteDocumentCommand command, CancellationToken cancellationToken)
    {
        var kind = NumberedDocuments.Find(command.Kind)!;
        var deletion = kind.Deletion!;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var target = await connection.QuerySingleOrDefaultAsync<Target>(new CommandDefinition(
            $"SELECT id AS Id, status AS Status, {deletion.DateColumn} AS Date, {kind.NumberColumn} AS Number FROM {kind.Table} WHERE id = @Id FOR UPDATE;",
            new { command.Id }, transaction, cancellationToken: cancellationToken));
        if (target is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure(new Error("Document.NotFound", "The document was not found."));
        }

        if (!Deletable.Contains(target.Status))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure(new Error("Document.NotDeletable", $"Document {target.Number} is {target.Status}: only a draft or a voided document can be deleted. Void it first."));
        }

        if (target.Status == "VOID" && await _periodLock.IsLockedAsync(target.Date.Year, target.Date.Month, deletion.PeriodModule, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure(new Error("PeriodLock.Locked", $"Period {target.Date:yyyy-MM} is locked; its documents stay as they are."));
        }

        // What goes along with the document (a voided invoice's credit note), or what stops the deletion (payments, returns, claims).
        var companions = new List<Guid>();
        var blocked = command.Kind switch
        {
            NumberedDocuments.Invoices => await CheckInvoiceAsync(connection, transaction, target, companions, cancellationToken),
            NumberedDocuments.PurchaseInvoices => await CheckPurchaseInvoiceAsync(connection, transaction, target, cancellationToken),
            _ => null
        };
        if (blocked is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure(blocked);
        }

        foreach (var id in companions.Append(target.Id))
        {
            await DeleteRecordedAsync(connection, transaction, kind, id, command.Reason.Trim(), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    private static async Task<Error?> CheckInvoiceAsync(DbConnection connection, DbTransaction transaction, Target target, List<Guid> companions, CancellationToken ct)
    {
        var type = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT invoice_type FROM invoices WHERE id = @Id;", new { target.Id }, transaction, cancellationToken: ct));
        if (type == "CREDIT_NOTE")
        {
            return new Error("Document.PartOfVoid", "A credit note records the void of an invoice; it is deleted together with that invoice.");
        }

        // The void's credit note leaves with the voided invoice; any other document built on it (a return) keeps it.
        companions.AddRange(await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT id FROM invoices WHERE original_invoice_id = @Id AND invoice_type = 'CREDIT_NOTE';", new { target.Id }, transaction, cancellationToken: ct)));
        var family = companions.Append(target.Id).ToArray();

        var dependants = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT string_agg(invoice_number, '، ') FROM invoices
            WHERE original_invoice_id = ANY(@family) AND NOT (id = ANY(@family));
            """,
            new { family }, transaction, cancellationToken: ct));
        if (dependants is not null)
        {
            return new Error("Document.HasDependants", $"Documents {dependants} refer to this invoice; delete them first.");
        }

        if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM payment_allocations WHERE invoice_id = ANY(@family));", new { family }, transaction, cancellationToken: ct)))
        {
            return new Error("Document.HasPayments", "Payments were allocated to this invoice at some point; it stays as part of their history.");
        }

        if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (
                    SELECT 1 FROM warranty_records
                    WHERE replacement_invoice_id = ANY(@family) OR (invoice_id = ANY(@family) AND (claim_date IS NOT NULL OR status IN ('CLAIMED', 'REJECTED'))));
                """,
                new { family }, transaction, cancellationToken: ct)))
        {
            return new Error("Document.HasWarrantyClaims", "A warranty claim refers to this invoice; it stays as part of that claim.");
        }

        // Unclaimed warranties of a voided sale go with it.
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM warranty_records WHERE invoice_id = ANY(@family);", new { family }, transaction, cancellationToken: ct));
        return null;
    }

    private static async Task<Error?> CheckPurchaseInvoiceAsync(DbConnection connection, DbTransaction transaction, Target target, CancellationToken ct)
    {
        var returns = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT string_agg(bill_number, '، ') FROM purchase_invoices WHERE return_against_id = @Id;", new { target.Id }, transaction, cancellationToken: ct));
        if (returns is not null)
        {
            return new Error("Document.HasDependants", $"Returns {returns} refer to this bill; delete them first.");
        }

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM supplier_payment_allocations WHERE purchase_invoice_id = @Id);", new { target.Id }, transaction, cancellationToken: ct))
            ? new Error("Document.HasPayments", "Supplier payments were allocated to this bill at some point; it stays as part of their history.")
            : null;
    }

    /// <summary>Records the deletion (header and lines as they were) and then deletes; the database refuses the delete without the record.</summary>
    private async Task DeleteRecordedAsync(DbConnection connection, DbTransaction transaction, NumberedDocumentKind kind, Guid id, string reason, CancellationToken ct)
    {
        var deletion = kind.Deletion!;
        await connection.ExecuteAsync(new CommandDefinition(
            $"""
            INSERT INTO deleted_documents (series_code, serial_no, document_number, document_table, document_id, status_at_deletion, snapshot, reason, deleted_by)
            SELECT h.series_code, h.serial_no, h.{kind.NumberColumn}, '{kind.Table}', h.id, h.status,
                   jsonb_build_object(
                       'header', to_jsonb(h),
                       'lines', COALESCE((SELECT jsonb_agg(to_jsonb(l) ORDER BY l.line_number) FROM {deletion.LinesTable} l WHERE l.{deletion.LinesForeignKey} = h.id), '[]'::jsonb)),
                   @reason, @By
            FROM {kind.Table} h WHERE h.id = @id;

            DELETE FROM {deletion.LinesTable} WHERE {deletion.LinesForeignKey} = @id;
            DELETE FROM {kind.Table} WHERE id = @id;
            """,
            new { id, reason, By = _currentUser.UserId }, transaction, cancellationToken: ct));

        if (kind.Kind == NumberedDocuments.JournalEntries)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM accounting_tag_links WHERE target_type = 'JOURNAL_ENTRY' AND target_key = @Key;",
                new { Key = id.ToString() }, transaction, cancellationToken: ct));
        }
    }
}

// ------------------------------------------------------------------ deleted documents and the numbering check

public sealed record GetDeletedDocumentsQuery(int PageNumber, int PageSize, string? SeriesCode) : IRequest<Result<PagedResponse<DeletedDocumentDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.AuditRead;
}

public sealed class GetDeletedDocumentsQueryHandler : IRequestHandler<GetDeletedDocumentsQuery, Result<PagedResponse<DeletedDocumentDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetDeletedDocumentsQueryHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    private sealed record Row(
        Guid Id, string SeriesCode, string SeriesNameAr, long SerialNo, string DocumentNumber, string DocumentTable, Guid DocumentId,
        string StatusAtDeletion, string Reason, Guid DeletedBy, string? DeletedByName, DateTimeOffset DeletedAt);

    public async Task<Result<PagedResponse<DeletedDocumentDto>>> Handle(GetDeletedDocumentsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(request.PageNumber, 1);
        var size = Math.Clamp(request.PageSize, 1, 200);
        var args = new { SeriesCode = string.IsNullOrWhiteSpace(request.SeriesCode) ? null : request.SeriesCode.Trim(), Offset = (page - 1) * size, Size = size };
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT d.id AS Id, d.series_code AS SeriesCode, s.name_ar AS SeriesNameAr, d.serial_no AS SerialNo, d.document_number AS DocumentNumber,
                   d.document_table AS DocumentTable, d.document_id AS DocumentId, d.status_at_deletion AS StatusAtDeletion, d.reason AS Reason,
                   d.deleted_by AS DeletedBy, COALESCE(NULLIF(u.full_name, ''), u.user_name) AS DeletedByName, d.deleted_at AS DeletedAt
            FROM deleted_documents d
            INNER JOIN document_series s ON s.code = d.series_code
            LEFT JOIN asp_net_users u ON u.id = d.deleted_by
            WHERE @SeriesCode::text IS NULL OR d.series_code = @SeriesCode
            ORDER BY d.deleted_at DESC
            OFFSET @Offset LIMIT @Size;
            """,
            args, cancellationToken: cancellationToken));
        var total = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM deleted_documents WHERE @SeriesCode::text IS NULL OR series_code = @SeriesCode;", args, cancellationToken: cancellationToken));

        var kinds = NumberedDocuments.All.ToDictionary(k => k.Table, k => k.Kind);
        var items = rows.Select(r => new DeletedDocumentDto(
            r.Id, r.SeriesCode, r.SeriesNameAr, r.SerialNo, r.DocumentNumber, kinds.GetValueOrDefault(r.DocumentTable, r.DocumentTable), r.DocumentId,
            r.StatusAtDeletion, r.Reason, r.DeletedBy, r.DeletedByName, r.DeletedAt)).ToList();
        return Result<PagedResponse<DeletedDocumentDto>>.Success(new PagedResponse<DeletedDocumentDto>(items, page, size, total));
    }
}

public sealed record GetNumberingHealthQuery : IRequest<Result<IReadOnlyList<DocumentSeriesHealthDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.AuditRead;
}

public sealed class GetNumberingHealthQueryHandler : IRequestHandler<GetNumberingHealthQuery, Result<IReadOnlyList<DocumentSeriesHealthDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetNumberingHealthQueryHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result<IReadOnlyList<DocumentSeriesHealthDto>>> Handle(GetNumberingHealthQuery request, CancellationToken cancellationToken)
    {
        var live = string.Join("\n    UNION ALL ", NumberedDocuments.All.Select(k => $"SELECT series_code FROM {k.Table}"));
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<DocumentSeriesHealthDto>(new CommandDefinition(
            $"""
            WITH live AS (SELECT series_code, count(*) AS n FROM ({live}) t GROUP BY series_code),
                 gone AS (SELECT series_code, count(*) AS n FROM deleted_documents GROUP BY series_code)
            SELECT s.code AS Code, s.prefix AS Prefix, s.name_ar AS NameAr, s.last_number AS LastNumber,
                   COALESCE(l.n, 0) AS Live, COALESCE(g.n, 0) AS Deleted, s.last_number - COALESCE(l.n, 0) - COALESCE(g.n, 0) AS Unexplained
            FROM document_series s
            LEFT JOIN live l ON l.series_code = s.code
            LEFT JOIN gone g ON g.series_code = s.code
            ORDER BY s.code;
            """,
            cancellationToken: cancellationToken));
        return Result<IReadOnlyList<DocumentSeriesHealthDto>>.Success(rows.ToList());
    }
}

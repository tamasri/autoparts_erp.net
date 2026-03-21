namespace AutoPartsERP.Application.Features.Receiving;

public sealed record GetReceivingDocumentsQuery(int PageNumber = 1, int PageSize = 20)
    : IRequest<Result<PagedResponse<ReceivingDocumentDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Receiving.Read;
}

public sealed class GetReceivingDocumentsQueryHandler : IRequestHandler<GetReceivingDocumentsQuery, Result<PagedResponse<ReceivingDocumentDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetReceivingDocumentsQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<ReceivingDocumentDto>>> Handle(GetReceivingDocumentsQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = request.PageSize <= 0 ? 20 : Math.Min(request.PageSize, 100);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var rows = (await connection.QueryAsync<ReceivingDocumentRow>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    document_no AS DocumentNo,
                    vendor_party_id AS VendorPartyId,
                    purchase_order_ref AS PurchaseOrderRef,
                    warehouse_id AS WarehouseId,
                    status AS Status,
                    received_by AS ReceivedBy,
                    received_at AS ReceivedAt,
                    posted_at AS PostedAt,
                    notes AS Notes,
                    COUNT(*) OVER() AS TotalCount
                FROM receiving_documents
                ORDER BY created_at DESC
                OFFSET @Offset
                LIMIT @PageSize;
                """,
                new { Offset = (pageNumber - 1) * pageSize, PageSize = pageSize },
                cancellationToken: cancellationToken))).ToArray();

        var items = rows.Select(x => new ReceivingDocumentDto(
            x.Id,
            x.DocumentNo,
            x.VendorPartyId,
            x.PurchaseOrderRef,
            x.WarehouseId,
            x.Status,
            x.ReceivedBy,
            x.ReceivedAt,
            x.PostedAt,
            x.Notes,
            Array.Empty<ReceivingLineDto>())).ToArray();

        var totalCount = rows.Length == 0 ? 0 : rows[0].TotalCount;
        return Result<PagedResponse<ReceivingDocumentDto>>.Success(new PagedResponse<ReceivingDocumentDto>(items, pageNumber, pageSize, totalCount));
    }

    private sealed record ReceivingDocumentRow(
        Guid Id,
        string DocumentNo,
        Guid? VendorPartyId,
        string? PurchaseOrderRef,
        Guid WarehouseId,
        string Status,
        Guid ReceivedBy,
        DateTimeOffset? ReceivedAt,
        DateTimeOffset? PostedAt,
        string? Notes,
        long TotalCount);
}

public sealed record CreateReceivingDocumentCommand(CreateReceivingDocumentRequest Request, string IdempotencyKey)
    : IRequest<Result<ReceivingDocumentDto>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Receiving.Create;
    public string AuditModule => "RECEIVING";
}

public sealed class CreateReceivingDocumentCommandValidator : AbstractValidator<CreateReceivingDocumentCommand>
{
    public CreateReceivingDocumentCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Request.WarehouseId).NotEmpty();
    }
}

public sealed class CreateReceivingDocumentCommandHandler : IRequestHandler<CreateReceivingDocumentCommand, Result<ReceivingDocumentDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreateReceivingDocumentCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<ReceivingDocumentDto>> Handle(CreateReceivingDocumentCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var documentNo = await connection.QuerySingleAsync<string>(
            new CommandDefinition(
                "SELECT 'RCV-' || to_char(CURRENT_DATE, 'YYYY') || '-' || lpad(nextval('receiving_document_seq')::text, 5, '0');",
                cancellationToken: cancellationToken));

        var dto = await connection.QuerySingleAsync<ReceivingDocumentDto>(
            new CommandDefinition(
                """
                INSERT INTO receiving_documents (
                    id, document_no, vendor_party_id, purchase_order_ref,
                    warehouse_id, status, received_by, notes, created_at, created_by)
                VALUES (
                    @Id, @DocumentNo, @VendorPartyId, @PurchaseOrderRef,
                    @WarehouseId, 'DRAFT', @ReceivedBy, @Notes, now(), @CreatedBy)
                RETURNING
                    id AS Id,
                    document_no AS DocumentNo,
                    vendor_party_id AS VendorPartyId,
                    purchase_order_ref AS PurchaseOrderRef,
                    warehouse_id AS WarehouseId,
                    status AS Status,
                    received_by AS ReceivedBy,
                    received_at AS ReceivedAt,
                    posted_at AS PostedAt,
                    notes AS Notes;
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    DocumentNo = documentNo,
                    request.Request.VendorPartyId,
                    request.Request.PurchaseOrderRef,
                    request.Request.WarehouseId,
                    ReceivedBy = _currentUser.UserId,
                    request.Request.Notes,
                    CreatedBy = _currentUser.UserId
                },
                cancellationToken: cancellationToken));

        return Result<ReceivingDocumentDto>.Success(dto with { Lines = Array.Empty<ReceivingLineDto>() });
    }
}

public sealed record AddReceivingLineCommand(Guid ReceivingDocumentId, AddReceivingLineRequest Request)
    : IRequest<Result<ReceivingLineDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Receiving.Create;
    public string AuditModule => "RECEIVING";
}

public sealed class AddReceivingLineCommandValidator : AbstractValidator<AddReceivingLineCommand>
{
    public AddReceivingLineCommandValidator()
    {
        RuleFor(x => x.ReceivingDocumentId).NotEmpty();
        RuleFor(x => x.Request.ItemId).NotEmpty();
        RuleFor(x => x.Request.ReceivedQty).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.RejectedQty).GreaterThanOrEqualTo(0);
    }
}

public sealed class AddReceivingLineCommandHandler : IRequestHandler<AddReceivingLineCommand, Result<ReceivingLineDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AddReceivingLineCommandHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<ReceivingLineDto>> Handle(AddReceivingLineCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var dto = await connection.QuerySingleAsync<ReceivingLineDto>(
            new CommandDefinition(
                """
                INSERT INTO receiving_lines (
                    id, receiving_document_id, item_id, expected_qty, received_qty,
                    rejected_qty, batch_id, assigned_location_id, condition_status,
                    manufacturer_part_match_ok, notes, created_at)
                VALUES (
                    @Id, @ReceivingDocumentId, @ItemId, @ExpectedQty, @ReceivedQty,
                    @RejectedQty, @BatchId, @AssignedLocationId, @ConditionStatus,
                    @ManufacturerPartMatchOk, @Notes, now())
                RETURNING
                    id AS Id,
                    receiving_document_id AS ReceivingDocumentId,
                    item_id AS ItemId,
                    expected_qty AS ExpectedQty,
                    received_qty AS ReceivedQty,
                    rejected_qty AS RejectedQty,
                    batch_id AS BatchId,
                    assigned_location_id AS AssignedLocationId,
                    condition_status AS ConditionStatus,
                    manufacturer_part_match_ok AS ManufacturerPartMatchOk,
                    notes AS Notes;
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    ReceivingDocumentId = request.ReceivingDocumentId,
                    request.Request.ItemId,
                    request.Request.ExpectedQty,
                    request.Request.ReceivedQty,
                    request.Request.RejectedQty,
                    request.Request.BatchId,
                    request.Request.AssignedLocationId,
                    ConditionStatus = string.IsNullOrWhiteSpace(request.Request.ConditionStatus)
                        ? "GOOD"
                        : request.Request.ConditionStatus.Trim().ToUpperInvariant(),
                    request.Request.ManufacturerPartMatchOk,
                    request.Request.Notes
                },
                cancellationToken: cancellationToken));

        return Result<ReceivingLineDto>.Success(dto);
    }
}

public sealed record PostReceivingDocumentCommand(Guid ReceivingDocumentId)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Receiving.Post;
    public string AuditModule => "RECEIVING";
}

public sealed class PostReceivingDocumentCommandHandler : IRequestHandler<PostReceivingDocumentCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public PostReceivingDocumentCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(PostReceivingDocumentCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var header = await connection.QuerySingleOrDefaultAsync<(Guid Id, Guid WarehouseId, string Status)>(
            new CommandDefinition(
                """
                SELECT id AS Id, warehouse_id AS WarehouseId, status AS Status
                FROM receiving_documents
                WHERE id = @Id
                FOR UPDATE;
                """,
                new { Id = request.ReceivingDocumentId },
                transaction,
                cancellationToken: cancellationToken));

        if (header.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Receiving.NotFound", "Receiving document was not found."));
        }

        if (header.Status is "COMPLETED" or "CANCELLED")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Receiving.InvalidState", "Receiving document is already finalized."));
        }

        var lines = (await connection.QueryAsync<(Guid Id, Guid ItemId, Guid? BatchId, decimal ReceivedQty, Guid? AssignedLocationId)>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    item_id AS ItemId,
                    batch_id AS BatchId,
                    received_qty AS ReceivedQty,
                    assigned_location_id AS AssignedLocationId
                FROM receiving_lines
                WHERE receiving_document_id = @Id;
                """,
                new { Id = request.ReceivingDocumentId },
                transaction,
                cancellationToken: cancellationToken))).ToArray();

        if (lines.Length == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Receiving.NoLines", "Receiving document must include at least one line."));
        }

        foreach (var line in lines)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO inventory_balances (id, item_id, location_id, batch_id, status, qty, updated_at)
                VALUES (@Id, @ItemId, @LocationId, @BatchId, 'RECEIVING', @Qty, now())
                ON CONFLICT (item_id, location_id, batch_id, status)
                DO UPDATE SET qty = inventory_balances.qty + @Qty, updated_at = now();
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    line.ItemId,
                    LocationId = line.AssignedLocationId ?? header.WarehouseId,
                    line.BatchId,
                    Qty = line.ReceivedQty
                },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO inventory_movements (
                    id, item_id, location_id, batch_id, movement_type, qty, direction,
                    from_status, to_status, reference_type, reference_id, performed_by, correlation_id, notes, created_at)
                VALUES (
                    @Id, @ItemId, @LocationId, @BatchId, 'RECEIPT', @Qty, 'IN',
                    NULL, 'RECEIVING', 'RECEIVING', @ReferenceId, @PerformedBy, @CorrelationId, @Notes, now());
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    line.ItemId,
                    LocationId = line.AssignedLocationId ?? header.WarehouseId,
                    line.BatchId,
                    Qty = line.ReceivedQty,
                    ReferenceId = request.ReceivingDocumentId,
                    PerformedBy = _currentUser.UserId,
                    CorrelationId = _currentUser.CorrelationId,
                    Notes = "Receiving posted"
                },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO putaway_tasks (
                    id, receiving_line_id, from_location_id, to_location_id, qty, status, assigned_to, created_at)
                VALUES (
                    @Id, @ReceivingLineId, @FromLocationId, @ToLocationId, @Qty, 'PENDING', NULL, now());
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    ReceivingLineId = line.Id,
                    FromLocationId = header.WarehouseId,
                    ToLocationId = line.AssignedLocationId ?? header.WarehouseId,
                    Qty = line.ReceivedQty
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE receiving_documents
            SET status = 'COMPLETED',
                received_at = now(),
                posted_at = now()
            WHERE id = @Id;
            """,
            new { Id = request.ReceivingDocumentId },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(request.ReceivingDocumentId);
    }
}

public sealed record GetPutawayTasksQuery(Guid ReceivingDocumentId)
    : IRequest<Result<IReadOnlyCollection<PutawayTaskDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Receiving.Putaway;
}

public sealed class GetPutawayTasksQueryHandler : IRequestHandler<GetPutawayTasksQuery, Result<IReadOnlyCollection<PutawayTaskDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetPutawayTasksQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<IReadOnlyCollection<PutawayTaskDto>>> Handle(GetPutawayTasksQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<PutawayTaskDto>(
            new CommandDefinition(
                """
                SELECT
                    pt.id AS Id,
                    pt.receiving_line_id AS ReceivingLineId,
                    pt.from_location_id AS FromLocationId,
                    pt.to_location_id AS ToLocationId,
                    pt.qty AS Qty,
                    pt.status AS Status,
                    pt.assigned_to AS AssignedTo,
                    pt.confirmed_by AS ConfirmedBy,
                    pt.confirmed_at AS ConfirmedAt
                FROM putaway_tasks pt
                INNER JOIN receiving_lines rl ON rl.id = pt.receiving_line_id
                WHERE rl.receiving_document_id = @ReceivingDocumentId
                ORDER BY pt.created_at ASC;
                """,
                new { request.ReceivingDocumentId },
                cancellationToken: cancellationToken));

        return Result<IReadOnlyCollection<PutawayTaskDto>>.Success(rows.ToArray());
    }
}

public sealed record CompletePutawayTaskCommand(Guid TaskId, CompletePutawayTaskRequest Request)
    : IRequest<Result>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Receiving.Putaway;
    public string AuditModule => "RECEIVING";
}

public sealed class CompletePutawayTaskCommandHandler : IRequestHandler<CompletePutawayTaskCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CompletePutawayTaskCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(CompletePutawayTaskCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var task = await connection.QuerySingleOrDefaultAsync<(Guid Id, Guid ReceivingLineId, Guid FromLocationId, decimal Qty, string Status)>(
            new CommandDefinition(
                """
                SELECT id AS Id, receiving_line_id AS ReceivingLineId, from_location_id AS FromLocationId, qty AS Qty, status AS Status
                FROM putaway_tasks
                WHERE id = @Id
                FOR UPDATE;
                """,
                new { Id = request.TaskId },
                transaction,
                cancellationToken: cancellationToken));

        if (task.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure(new Error("PutawayTask.NotFound", "Putaway task not found."));
        }

        var line = await connection.QuerySingleOrDefaultAsync<(Guid ItemId, Guid? BatchId, Guid? AssignedLocationId)>(
            new CommandDefinition(
                """
                SELECT item_id AS ItemId, batch_id AS BatchId, assigned_location_id AS AssignedLocationId
                FROM receiving_lines
                WHERE id = @Id;
                """,
                new { Id = task.ReceivingLineId },
                transaction,
                cancellationToken: cancellationToken));

        if (line.ItemId == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure(new Error("ReceivingLine.NotFound", "Receiving line not found."));
        }

        var qty = request.Request.Qty <= 0 ? task.Qty : request.Request.Qty;
        var toLocation = request.Request.ToLocationId == Guid.Empty
            ? line.AssignedLocationId ?? task.FromLocationId
            : request.Request.ToLocationId;

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE inventory_balances
            SET qty = GREATEST(0, qty - @Qty), updated_at = now()
            WHERE item_id = @ItemId
              AND location_id = @FromLocationId
              AND ((@BatchId IS NULL AND batch_id IS NULL) OR batch_id = @BatchId)
              AND status = 'RECEIVING';
            """,
            new
            {
                line.ItemId,
                FromLocationId = task.FromLocationId,
                line.BatchId,
                Qty = qty
            },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO inventory_balances (id, item_id, location_id, batch_id, status, qty, updated_at)
            VALUES (@Id, @ItemId, @ToLocationId, @BatchId, 'AVAILABLE', @Qty, now())
            ON CONFLICT (item_id, location_id, batch_id, status)
            DO UPDATE SET qty = inventory_balances.qty + @Qty, updated_at = now();
            """,
            new
            {
                Id = Guid.NewGuid(),
                line.ItemId,
                ToLocationId = toLocation,
                line.BatchId,
                Qty = qty
            },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO inventory_movements (
                id, item_id, location_id, batch_id, movement_type, qty, direction,
                from_status, to_status, reference_type, reference_id, performed_by, correlation_id, notes, created_at)
            VALUES (
                @Id, @ItemId, @LocationId, @BatchId, 'STATUS_CHANGE', @Qty, 'IN',
                'RECEIVING', 'AVAILABLE', 'PUTAWAY', @ReferenceId, @PerformedBy, @CorrelationId, @Notes, now());
            """,
            new
            {
                Id = Guid.NewGuid(),
                line.ItemId,
                LocationId = toLocation,
                line.BatchId,
                Qty = qty,
                ReferenceId = request.TaskId,
                PerformedBy = _currentUser.UserId,
                CorrelationId = _currentUser.CorrelationId,
                Notes = "Putaway completed"
            },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE putaway_tasks
            SET status = 'COMPLETED',
                to_location_id = @ToLocationId,
                confirmed_by = @ConfirmedBy,
                confirmed_at = now()
            WHERE id = @Id;
            """,
            new
            {
                Id = request.TaskId,
                ToLocationId = toLocation,
                ConfirmedBy = _currentUser.UserId
            },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}

namespace AutoPartsERP.Application.Features.StockAdjustments;

public sealed record GetStockAdjustmentsQuery(int PageNumber = 1, int PageSize = 20)
    : IRequest<Result<PagedResponse<StockAdjustmentDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.StockAdjustments.Read;
}

public sealed class GetStockAdjustmentsQueryHandler : IRequestHandler<GetStockAdjustmentsQuery, Result<PagedResponse<StockAdjustmentDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetStockAdjustmentsQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<StockAdjustmentDto>>> Handle(GetStockAdjustmentsQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = request.PageSize <= 0 ? 20 : Math.Min(request.PageSize, 100);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var rows = (await connection.QueryAsync<Row>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    adjustment_no AS AdjustmentNo,
                    adjustment_type AS AdjustmentType,
                    warehouse_id AS WarehouseId,
                    reason_code AS ReasonCode,
                    status AS Status,
                    posted_at AS PostedAt,
                    COUNT(*) OVER() AS TotalCount
                FROM stock_adjustments
                ORDER BY created_at DESC
                OFFSET @Offset LIMIT @PageSize;
                """,
                new { Offset = (pageNumber - 1) * pageSize, PageSize = pageSize },
                cancellationToken: cancellationToken))).ToArray();

        var items = rows.Select(x => new StockAdjustmentDto(
            x.Id,
            x.AdjustmentNo,
            x.AdjustmentType,
            x.WarehouseId,
            x.ReasonCode,
            x.Status,
            x.PostedAt,
            Array.Empty<StockAdjustmentLineDto>())).ToArray();

        var total = rows.Length == 0 ? 0 : rows[0].TotalCount;
        return Result<PagedResponse<StockAdjustmentDto>>.Success(new PagedResponse<StockAdjustmentDto>(items, pageNumber, pageSize, total));
    }

    private sealed record Row(
        Guid Id,
        string AdjustmentNo,
        string AdjustmentType,
        Guid WarehouseId,
        string ReasonCode,
        string Status,
        DateTimeOffset? PostedAt,
        long TotalCount);
}

public sealed record CreateStockAdjustmentCommand(CreateStockAdjustmentRequest Request)
    : IRequest<Result<StockAdjustmentDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.StockAdjustments.Create;
    public string AuditModule => "INVENTORY";
}

public sealed class CreateStockAdjustmentCommandValidator : AbstractValidator<CreateStockAdjustmentCommand>
{
    public CreateStockAdjustmentCommandValidator()
    {
        RuleFor(x => x.Request.AdjustmentType).NotEmpty();
        RuleFor(x => x.Request.WarehouseId).NotEmpty();
        RuleFor(x => x.Request.ReasonCode).NotEmpty();
        RuleFor(x => x.Request.Lines).NotEmpty();
    }
}

public sealed class CreateStockAdjustmentCommandHandler : IRequestHandler<CreateStockAdjustmentCommand, Result<StockAdjustmentDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreateStockAdjustmentCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<StockAdjustmentDto>> Handle(CreateStockAdjustmentCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var adjustmentNo = await connection.QuerySingleAsync<string>(
            new CommandDefinition(
                "SELECT 'ADJ-' || to_char(CURRENT_DATE, 'YYYY') || '-' || lpad(nextval('stock_adjustment_seq')::text, 5, '0');",
                transaction: transaction,
                cancellationToken: cancellationToken));

        var adjustmentId = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO stock_adjustments (
                id, adjustment_no, adjustment_type, warehouse_id, reason_code, status, created_at, created_by)
            VALUES (
                @Id, @AdjustmentNo, @AdjustmentType, @WarehouseId, @ReasonCode, 'DRAFT', now(), @CreatedBy);
            """,
            new
            {
                Id = adjustmentId,
                AdjustmentNo = adjustmentNo,
                AdjustmentType = request.Request.AdjustmentType.Trim().ToUpperInvariant(),
                request.Request.WarehouseId,
                request.Request.ReasonCode,
                CreatedBy = _currentUser.UserId
            },
            transaction,
            cancellationToken: cancellationToken));

        foreach (var line in request.Request.Lines)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO stock_adjustment_lines (
                    id, stock_adjustment_id, item_id, location_id, batch_id, status,
                    qty_delta, system_qty_before, system_qty_after, notes)
                VALUES (
                    @Id, @StockAdjustmentId, @ItemId, @LocationId, @BatchId, @Status,
                    @QtyDelta, @SystemQtyBefore, @SystemQtyAfter, @Notes);
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    StockAdjustmentId = adjustmentId,
                    line.ItemId,
                    line.LocationId,
                    line.BatchId,
                    line.Status,
                    line.QtyDelta,
                    line.SystemQtyBefore,
                    line.SystemQtyAfter,
                    line.Notes
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<StockAdjustmentDto>.Success(new StockAdjustmentDto(
            adjustmentId,
            adjustmentNo,
            request.Request.AdjustmentType.Trim().ToUpperInvariant(),
            request.Request.WarehouseId,
            request.Request.ReasonCode,
            "DRAFT",
            null,
            Array.Empty<StockAdjustmentLineDto>()));
    }
}

public sealed record PostStockAdjustmentCommand(Guid StockAdjustmentId, DateOnly PostingDate)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest, IPeriodSensitiveRequest
{
    public string RequiredPermission => PermissionCodes.StockAdjustments.Post;
    public string AuditModule => "INVENTORY";
    public DateTimeOffset OperationDate => PostingDate.ToDateTime(TimeOnly.MinValue);
    public string Module => "INVENTORY";
}

public sealed class PostStockAdjustmentCommandHandler : IRequestHandler<PostStockAdjustmentCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public PostStockAdjustmentCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(PostStockAdjustmentCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var header = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Status)>(
            new CommandDefinition(
                """
                SELECT id AS Id, status AS Status
                FROM stock_adjustments
                WHERE id = @Id
                FOR UPDATE;
                """,
                new { Id = request.StockAdjustmentId },
                transaction,
                cancellationToken: cancellationToken));

        if (header.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("StockAdjustment.NotFound", "Stock adjustment was not found."));
        }

        if (header.Status != "DRAFT")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("StockAdjustment.InvalidState", "Only draft adjustments can be posted."));
        }

        var lines = (await connection.QueryAsync<(Guid ItemId, Guid LocationId, Guid? BatchId, string Status, decimal QtyDelta)>(
            new CommandDefinition(
                """
                SELECT item_id AS ItemId, location_id AS LocationId, batch_id AS BatchId, status AS Status, qty_delta AS QtyDelta
                FROM stock_adjustment_lines
                WHERE stock_adjustment_id = @Id;
                """,
                new { Id = request.StockAdjustmentId },
                transaction,
                cancellationToken: cancellationToken))).ToArray();

        if (lines.Length == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("StockAdjustment.NoLines", "Stock adjustment must include at least one line."));
        }

        foreach (var line in lines)
        {
            if (line.QtyDelta >= 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO inventory_balances (id, item_id, location_id, batch_id, status, qty, updated_at)
                    VALUES (@Id, @ItemId, @LocationId, @BatchId, @Status, @Qty, now())
                    ON CONFLICT (item_id, location_id, batch_id, status)
                    DO UPDATE SET qty = inventory_balances.qty + @Qty, updated_at = now();
                    """,
                    new
                    {
                        Id = Guid.NewGuid(),
                        line.ItemId,
                        line.LocationId,
                        line.BatchId,
                        line.Status,
                        Qty = line.QtyDelta
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }
            else
            {
                var affected = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE inventory_balances
                    SET qty = qty + @QtyDelta, updated_at = now()
                    WHERE item_id = @ItemId
                      AND location_id = @LocationId
                      AND ((@BatchId IS NULL AND batch_id IS NULL) OR batch_id = @BatchId)
                      AND status = @Status
                      AND qty + @QtyDelta >= 0;
                    """,
                    new
                    {
                        line.ItemId,
                        line.LocationId,
                        line.BatchId,
                        line.Status,
                        line.QtyDelta
                    },
                    transaction,
                    cancellationToken: cancellationToken));

                if (affected == 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<Guid>.Failure(new Error("Stock.InsufficientQuantity", "Adjustment would make stock negative."));
                }
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO inventory_movements (
                    id, item_id, location_id, batch_id, movement_type, qty, direction,
                    from_status, to_status, reference_type, reference_id, performed_by, correlation_id, notes, created_at)
                VALUES (
                    @Id, @ItemId, @LocationId, @BatchId, 'ADJUSTMENT', @Qty, @Direction,
                    @Status, @Status, 'ADJUSTMENT', @ReferenceId, @PerformedBy, @CorrelationId, @Notes, now());
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    line.ItemId,
                    line.LocationId,
                    line.BatchId,
                    Qty = Math.Abs(line.QtyDelta),
                    Direction = line.QtyDelta >= 0 ? "IN" : "OUT",
                    line.Status,
                    ReferenceId = request.StockAdjustmentId,
                    PerformedBy = _currentUser.UserId,
                    CorrelationId = _currentUser.CorrelationId,
                    Notes = "Stock adjustment posted"
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE stock_adjustments
            SET status = 'POSTED',
                posted_at = now(),
                posted_by = @PostedBy
            WHERE id = @Id;
            """,
            new { Id = request.StockAdjustmentId, PostedBy = _currentUser.UserId },
            transaction,
            cancellationToken: cancellationToken));

        var outboxMessage = OutboxMessage.Create(
            OutboxEventTypes.StockAdjusted,
            "StockAdjustment",
            request.StockAdjustmentId,
            new
            {
                StockAdjustmentId = request.StockAdjustmentId,
                Lines = lines.Length
            },
            _currentUser.CorrelationId);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO outbox_messages (
                id, event_type, aggregate_type, aggregate_id, payload_json, occurred_at,
                processed_at, processing_error, retry_count, correlation_id)
            VALUES (
                @Id, @EventType, @AggregateType, @AggregateId, @PayloadJson, @OccurredAt,
                @ProcessedAt, @ProcessingError, @RetryCount, @CorrelationId);
            """,
            new
            {
                outboxMessage.Id,
                outboxMessage.EventType,
                outboxMessage.AggregateType,
                outboxMessage.AggregateId,
                outboxMessage.PayloadJson,
                outboxMessage.OccurredAt,
                outboxMessage.ProcessedAt,
                outboxMessage.ProcessingError,
                outboxMessage.RetryCount,
                outboxMessage.CorrelationId
            },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(request.StockAdjustmentId);
    }
}

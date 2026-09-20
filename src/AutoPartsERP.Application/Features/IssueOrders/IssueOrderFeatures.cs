namespace AutoPartsERP.Application.Features.IssueOrders;

public sealed record GetIssueOrdersQuery(int PageNumber = 1, int PageSize = 20)
    : IRequest<Result<PagedResponse<AutoPartsERP.Contracts.Wms.IssueOrderListDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.IssueOrders.Read;
}

public sealed class GetIssueOrdersQueryHandler : IRequestHandler<GetIssueOrdersQuery, Result<PagedResponse<AutoPartsERP.Contracts.Wms.IssueOrderListDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetIssueOrdersQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<AutoPartsERP.Contracts.Wms.IssueOrderListDto>>> Handle(GetIssueOrdersQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = request.PageSize <= 0 ? 20 : Math.Min(request.PageSize, 100);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var rows = (await connection.QueryAsync<Row>(
            new CommandDefinition(
                """
                SELECT id AS Id, order_no AS OrderNo, source_type AS SourceType, source_id AS SourceId, warehouse_id AS WarehouseId,
                       status AS Status, issued_at AS IssuedAt, created_at AS CreatedAt, COUNT(*) OVER() AS TotalCount
                FROM issue_orders
                ORDER BY created_at DESC
                OFFSET @Offset LIMIT @PageSize;
                """,
                new { Offset = (pageNumber - 1) * pageSize, PageSize = pageSize },
                cancellationToken: cancellationToken))).ToArray();

        var items = rows.Select(x => new AutoPartsERP.Contracts.Wms.IssueOrderListDto(
            x.Id, x.OrderNo, x.SourceType, x.SourceId, x.WarehouseId, x.Status, x.IssuedAt, x.CreatedAt)).ToArray();
        return Result<PagedResponse<AutoPartsERP.Contracts.Wms.IssueOrderListDto>>.Success(
            new PagedResponse<AutoPartsERP.Contracts.Wms.IssueOrderListDto>(items, pageNumber, pageSize, rows.Length == 0 ? 0 : rows[0].TotalCount));
    }

    private sealed record Row(
        Guid Id, string OrderNo, string SourceType, Guid? SourceId, Guid WarehouseId, string Status,
        DateTimeOffset? IssuedAt, DateTimeOffset CreatedAt, long TotalCount);
}

public sealed record CreateIssueOrderCommand(
    string SourceType,
    Guid? SourceId,
    Guid WarehouseId,
    IReadOnlyCollection<CreateIssueOrderLine> Lines,
    string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.IssueOrders.Create;
    public string AuditModule => "ISSUE_ORDERS";
}

public sealed record CreateIssueOrderLine(
    Guid ItemId,
    decimal RequestedQty,
    Guid? SourceLocationId,
    Guid? BatchId);

public sealed class CreateIssueOrderCommandValidator : AbstractValidator<CreateIssueOrderCommand>
{
    public CreateIssueOrderCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.SourceType).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty();
    }
}

public sealed class CreateIssueOrderCommandHandler : IRequestHandler<CreateIssueOrderCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreateIssueOrderCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(CreateIssueOrderCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var orderNo = await connection.QuerySingleAsync<string>(
            new CommandDefinition(
                "SELECT 'ISO-' || to_char(CURRENT_DATE, 'YYYY') || '-' || lpad(nextval('issue_order_seq')::text, 5, '0');",
                transaction: transaction,
                cancellationToken: cancellationToken));

        var orderId = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO issue_orders (
                id, order_no, source_type, source_id, warehouse_id, status, created_by, created_at)
            VALUES (
                @Id, @OrderNo, @SourceType, @SourceId, @WarehouseId, 'DRAFT', @CreatedBy, now());
            """,
            new
            {
                Id = orderId,
                OrderNo = orderNo,
                SourceType = request.SourceType.Trim().ToUpperInvariant(),
                request.SourceId,
                request.WarehouseId,
                CreatedBy = _currentUser.UserId
            },
            transaction,
            cancellationToken: cancellationToken));

        foreach (var line in request.Lines)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO issue_order_lines (
                    id, issue_order_id, item_id, requested_qty, picked_qty, verified_qty, issued_qty, source_location_id, batch_id)
                VALUES (
                    @Id, @IssueOrderId, @ItemId, @RequestedQty, 0, 0, 0, @SourceLocationId, @BatchId);
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    IssueOrderId = orderId,
                    line.ItemId,
                    line.RequestedQty,
                    line.SourceLocationId,
                    line.BatchId
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(orderId);
    }
}

public sealed record GeneratePickTasksCommand(Guid IssueOrderId)
    : IRequest<Result>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.IssueOrders.Pick;
    public string AuditModule => "ISSUE_ORDERS";
}

public sealed class GeneratePickTasksCommandHandler : IRequestHandler<GeneratePickTasksCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GeneratePickTasksCommandHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result> Handle(GeneratePickTasksCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO pick_tasks (
                id, issue_order_line_id, location_id, qty, pick_sequence, status, assigned_to, created_at)
            SELECT
                uuid_generate_v4(),
                iol.id,
                COALESCE(iol.source_location_id, io.warehouse_id),
                iol.requested_qty,
                ROW_NUMBER() OVER (ORDER BY iol.id),
                'PENDING',
                NULL,
                now()
            FROM issue_order_lines iol
            INNER JOIN issue_orders io ON io.id = iol.issue_order_id
            WHERE iol.issue_order_id = @IssueOrderId
              AND NOT EXISTS (
                SELECT 1 FROM pick_tasks p WHERE p.issue_order_line_id = iol.id
              );

            UPDATE issue_orders SET status = 'PICKING' WHERE id = @IssueOrderId;
            """,
            new { request.IssueOrderId },
            cancellationToken: cancellationToken));

        return Result.Success();
    }
}

public sealed record CompletePickTaskCommand(Guid IssueOrderId, Guid TaskId)
    : IRequest<Result>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.IssueOrders.Pick;
    public string AuditModule => "ISSUE_ORDERS";
}

public sealed class CompletePickTaskCommandHandler : IRequestHandler<CompletePickTaskCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CompletePickTaskCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(CompletePickTaskCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var task = await connection.QuerySingleOrDefaultAsync<(Guid Id, Guid IssueOrderLineId, decimal Qty)>(
            new CommandDefinition(
                """
                SELECT id AS Id, issue_order_line_id AS IssueOrderLineId, qty AS Qty
                FROM pick_tasks
                WHERE id = @TaskId
                FOR UPDATE;
                """,
                new { request.TaskId },
                transaction,
                cancellationToken: cancellationToken));

        if (task.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure(new Error("PickTask.NotFound", "Pick task was not found."));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE pick_tasks
            SET status = 'PICKED',
                picked_by = @PickedBy,
                picked_at = now()
            WHERE id = @TaskId;

            UPDATE issue_order_lines
            SET picked_qty = picked_qty + @Qty
            WHERE id = @IssueOrderLineId;
            """,
            new
            {
                request.TaskId,
                PickedBy = _currentUser.UserId,
                task.Qty,
                task.IssueOrderLineId
            },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record VerifyPickTaskCommand(Guid IssueOrderId, Guid TaskId)
    : IRequest<Result>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.IssueOrders.Verify;
    public string AuditModule => "ISSUE_ORDERS";
}

public sealed class VerifyPickTaskCommandHandler : IRequestHandler<VerifyPickTaskCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public VerifyPickTaskCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(VerifyPickTaskCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var task = await connection.QuerySingleOrDefaultAsync<(Guid Id, Guid IssueOrderLineId, decimal Qty)>(
            new CommandDefinition(
                """
                SELECT id AS Id, issue_order_line_id AS IssueOrderLineId, qty AS Qty
                FROM pick_tasks
                WHERE id = @TaskId
                FOR UPDATE;
                """,
                new { request.TaskId },
                transaction,
                cancellationToken: cancellationToken));

        if (task.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure(new Error("PickTask.NotFound", "Pick task was not found."));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE pick_tasks
            SET status = 'VERIFIED',
                verified_by = @VerifiedBy,
                verified_at = now()
            WHERE id = @TaskId;

            UPDATE issue_order_lines
            SET verified_qty = verified_qty + @Qty
            WHERE id = @IssueOrderLineId;

            UPDATE issue_orders SET status = 'VERIFYING' WHERE id = @IssueOrderId;
            """,
            new
            {
                request.TaskId,
                VerifiedBy = _currentUser.UserId,
                task.Qty,
                task.IssueOrderLineId,
                request.IssueOrderId
            },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record IssueOrderCommand(Guid IssueOrderId)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.IssueOrders.Issue;
    public string AuditModule => "ISSUE_ORDERS";
}

public sealed class IssueOrderCommandHandler : IRequestHandler<IssueOrderCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public IssueOrderCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Issuing an order is what takes the goods out of the warehouse: each verified quantity leaves stock at its source location,
    /// the line records what was issued and the movement is written to the item ledger. Before this the order only changed status.
    /// </summary>
    public async Task<Result<Guid>> Handle(IssueOrderCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var order = await connection.QuerySingleOrDefaultAsync<(Guid Id, Guid WarehouseId, string Status, string OrderNo)>(new CommandDefinition(
            "SELECT id AS Id, warehouse_id AS WarehouseId, status AS Status, order_no AS OrderNo FROM issue_orders WHERE id = @Id FOR UPDATE;",
            new { Id = request.IssueOrderId }, transaction, cancellationToken: cancellationToken));
        if (order.Id == Guid.Empty || order.Status is not ("PICKING" or "VERIFYING"))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("IssueOrder.InvalidState", "Issue order cannot be issued in current state."));
        }

        var lines = (await connection.QueryAsync<(Guid Id, Guid ItemId, Guid? SourceLocationId, Guid? BatchId, decimal Qty)>(new CommandDefinition(
            """
            SELECT id AS Id, item_id AS ItemId, source_location_id AS SourceLocationId, batch_id AS BatchId,
                   CASE WHEN verified_qty > 0 THEN verified_qty ELSE picked_qty END AS Qty
            FROM issue_order_lines
            WHERE issue_order_id = @Id;
            """,
            new { Id = request.IssueOrderId }, transaction, cancellationToken: cancellationToken))).ToList();

        if (lines.All(l => l.Qty <= 0))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("IssueOrder.NothingPicked", "Nothing has been picked and verified yet."));
        }

        foreach (var line in lines.Where(l => l.Qty > 0))
        {
            var locationId = line.SourceLocationId ?? order.WarehouseId;

            var sku = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT sku_id FROM items WHERE id = @ItemId;", new { line.ItemId }, transaction, cancellationToken: cancellationToken));
            if (sku is not null)
            {
                var onHand = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                    "SELECT quantity_on_hand FROM inventory_stock WHERE sku_id = @Sku AND location_id = @locationId FOR UPDATE;",
                    new { Sku = sku, locationId }, transaction, cancellationToken: cancellationToken));
                if (onHand is null || onHand < line.Qty)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<Guid>.Failure(new Error("Stock.InsufficientQuantity", "Insufficient stock quantity at the source location."));
                }

                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE inventory_stock SET quantity_on_hand = quantity_on_hand - @Qty, updated_at = now() WHERE sku_id = @Sku AND location_id = @locationId;",
                    new { line.Qty, Sku = sku, locationId }, transaction, cancellationToken: cancellationToken));
            }

            // Keep the warehouse-side balance in step immediately (the periodic sync would otherwise correct it later).
            await connection.ExecuteAsync(new CommandDefinition(
                """
                WITH target AS (
                    SELECT id FROM inventory_balances
                    WHERE item_id = @ItemId AND location_id = @locationId AND status = 'AVAILABLE' AND qty >= @Qty
                    ORDER BY qty DESC LIMIT 1)
                UPDATE inventory_balances b SET qty = b.qty - @Qty, updated_at = now() FROM target t WHERE b.id = t.id;
                """,
                new { line.ItemId, locationId, line.Qty }, transaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO inventory_movements (
                    id, item_id, location_id, batch_id, movement_type, qty, direction, from_status, to_status,
                    reference_type, reference_id, performed_by, correlation_id, notes, created_at)
                VALUES (uuid_generate_v4(), @ItemId, @locationId, @BatchId, 'ISSUE', @Qty, 'OUT', 'AVAILABLE', NULL,
                        'ISSUE_ORDER', @OrderId, @By, uuid_generate_v4(), @Notes, now());
                """,
                new { line.ItemId, locationId, line.BatchId, line.Qty, OrderId = order.Id, By = _currentUser.UserId, Notes = $"Issue order {order.OrderNo}" },
                transaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE issue_order_lines SET issued_qty = @Qty WHERE id = @Id;",
                new { line.Qty, line.Id }, transaction, cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE issue_orders SET status = 'ISSUED', issued_at = now() WHERE id = @Id;",
            new { Id = request.IssueOrderId }, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(request.IssueOrderId);
    }
}


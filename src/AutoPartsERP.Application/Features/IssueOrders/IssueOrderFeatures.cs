namespace AutoPartsERP.Application.Features.IssueOrders;

public sealed record GetIssueOrdersQuery(int PageNumber = 1, int PageSize = 20)
    : IRequest<Result<PagedResponse<object>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.IssueOrders.Read;
}

public sealed class GetIssueOrdersQueryHandler : IRequestHandler<GetIssueOrdersQuery, Result<PagedResponse<object>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetIssueOrdersQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<object>>> Handle(GetIssueOrdersQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = request.PageSize <= 0 ? 20 : Math.Min(request.PageSize, 100);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync(
            new CommandDefinition(
                """
                SELECT
                    id,
                    order_no AS orderNo,
                    source_type AS sourceType,
                    source_id AS sourceId,
                    warehouse_id AS warehouseId,
                    status,
                    issued_at AS issuedAt,
                    created_at AS createdAt
                FROM issue_orders
                ORDER BY created_at DESC
                OFFSET @Offset LIMIT @PageSize;
                """,
                new { Offset = (pageNumber - 1) * pageSize, PageSize = pageSize },
                cancellationToken: cancellationToken));

        var items = rows.Cast<object>().ToArray();
        return Result<PagedResponse<object>>.Success(new PagedResponse<object>(items, pageNumber, pageSize, items.LongLength));
    }
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

    public IssueOrderCommandHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<Guid>> Handle(IssueOrderCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE issue_orders
                SET status = 'ISSUED',
                    issued_at = now()
                WHERE id = @Id
                  AND status IN ('PICKING','VERIFYING');
                """,
                new { Id = request.IssueOrderId },
                cancellationToken: cancellationToken));

        return affected == 0
            ? Result<Guid>.Failure(new Error("IssueOrder.InvalidState", "Issue order cannot be issued in current state."))
            : Result<Guid>.Success(request.IssueOrderId);
    }
}


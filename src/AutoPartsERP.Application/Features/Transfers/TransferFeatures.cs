namespace AutoPartsERP.Application.Features.Transfers;

public sealed record GetTransferRequestsQuery(int PageNumber = 1, int PageSize = 20)
    : IRequest<Result<PagedResponse<object>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Transfers.Read;
}

public sealed class GetTransferRequestsQueryHandler : IRequestHandler<GetTransferRequestsQuery, Result<PagedResponse<object>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetTransferRequestsQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<object>>> Handle(GetTransferRequestsQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = request.PageSize <= 0 ? 20 : Math.Min(request.PageSize, 100);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync(
            new CommandDefinition(
                """
                SELECT
                    id,
                    source_warehouse_id AS sourceWarehouseId,
                    destination_warehouse_id AS destinationWarehouseId,
                    status,
                    requested_by AS requestedBy,
                    approved_by AS approvedBy,
                    approval_id AS approvalId,
                    notes,
                    created_at AS createdAt
                FROM transfer_requests
                ORDER BY created_at DESC
                OFFSET @Offset LIMIT @PageSize;
                """,
                new { Offset = (pageNumber - 1) * pageSize, PageSize = pageSize },
                cancellationToken: cancellationToken));

        var items = rows.Cast<object>().ToArray();
        return Result<PagedResponse<object>>.Success(new PagedResponse<object>(items, pageNumber, pageSize, items.LongLength));
    }
}

public sealed record CreateTransferRequestCommand(
    Guid SourceWarehouseId,
    Guid DestinationWarehouseId,
    IReadOnlyCollection<CreateTransferOrderLineRequest> Lines,
    string? Notes)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.Transfers.CreateRequest;
    public string AuditModule => "TRANSFERS";
    public bool RequiresApproval => true;
}

public sealed class CreateTransferRequestCommandValidator : AbstractValidator<CreateTransferRequestCommand>
{
    public CreateTransferRequestCommandValidator()
    {
        RuleFor(x => x.SourceWarehouseId).NotEmpty();
        RuleFor(x => x.DestinationWarehouseId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty();
    }
}

public sealed class CreateTransferRequestCommandHandler : IRequestHandler<CreateTransferRequestCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreateTransferRequestCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(CreateTransferRequestCommand request, CancellationToken cancellationToken)
    {
        if (request.SourceWarehouseId == request.DestinationWarehouseId)
        {
            return Result<Guid>.Failure(new Error("Transfer.InvalidWarehouses", "Source and destination warehouse must be different."));
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var requestId = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO transfer_requests (
                id, source_warehouse_id, destination_warehouse_id, status,
                requested_by, notes, created_at)
            VALUES (
                @Id, @SourceWarehouseId, @DestinationWarehouseId, 'PENDING_APPROVAL',
                @RequestedBy, @Notes, now());
            """,
            new
            {
                Id = requestId,
                request.SourceWarehouseId,
                request.DestinationWarehouseId,
                RequestedBy = _currentUser.UserId,
                request.Notes
            },
            transaction,
            cancellationToken: cancellationToken));

        foreach (var line in request.Lines)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO transfer_request_lines (
                    id, transfer_request_id, item_id, requested_qty, approved_qty)
                VALUES (@Id, @TransferRequestId, @ItemId, @RequestedQty, NULL);
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    TransferRequestId = requestId,
                    line.ItemId,
                    RequestedQty = line.ShippedQty
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(requestId);
    }
}

public sealed record GetTransferOrdersQuery(int PageNumber = 1, int PageSize = 20)
    : IRequest<Result<PagedResponse<TransferOrderDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Transfers.Read;
}

public sealed class GetTransferOrdersQueryHandler : IRequestHandler<GetTransferOrdersQuery, Result<PagedResponse<TransferOrderDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetTransferOrdersQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<TransferOrderDto>>> Handle(GetTransferOrdersQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = request.PageSize <= 0 ? 20 : Math.Min(request.PageSize, 100);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<TransferOrderRow>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    transfer_no AS TransferNo,
                    transfer_request_id AS TransferRequestId,
                    source_warehouse_id AS SourceWarehouseId,
                    destination_warehouse_id AS DestinationWarehouseId,
                    status AS Status,
                    shipped_at AS ShippedAt,
                    received_at AS ReceivedAt,
                    COUNT(*) OVER() AS TotalCount
                FROM transfer_orders
                ORDER BY created_at DESC
                OFFSET @Offset LIMIT @PageSize;
                """,
                new { Offset = (pageNumber - 1) * pageSize, PageSize = pageSize },
                cancellationToken: cancellationToken))).ToArray();

        var items = rows.Select(x => new TransferOrderDto(
            x.Id,
            x.TransferNo,
            x.TransferRequestId,
            x.SourceWarehouseId,
            x.DestinationWarehouseId,
            x.Status,
            x.ShippedAt,
            x.ReceivedAt,
            Array.Empty<TransferOrderLineDto>())).ToArray();

        var total = rows.Length == 0 ? 0 : rows[0].TotalCount;
        return Result<PagedResponse<TransferOrderDto>>.Success(new PagedResponse<TransferOrderDto>(items, pageNumber, pageSize, total));
    }

    private sealed record TransferOrderRow(
        Guid Id,
        string TransferNo,
        Guid? TransferRequestId,
        Guid SourceWarehouseId,
        Guid DestinationWarehouseId,
        string Status,
        DateTimeOffset? ShippedAt,
        DateTimeOffset? ReceivedAt,
        long TotalCount);
}

public sealed record CreateTransferOrderCommand(CreateTransferOrderRequest Request)
    : IRequest<Result<TransferOrderDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Transfers.CreateOrder;
    public string AuditModule => "TRANSFERS";
}

public sealed class CreateTransferOrderCommandValidator : AbstractValidator<CreateTransferOrderCommand>
{
    public CreateTransferOrderCommandValidator()
    {
        RuleFor(x => x.Request.SourceWarehouseId).NotEmpty();
        RuleFor(x => x.Request.DestinationWarehouseId).NotEmpty();
        RuleFor(x => x.Request.Lines).NotEmpty();
    }
}

public sealed class CreateTransferOrderCommandHandler : IRequestHandler<CreateTransferOrderCommand, Result<TransferOrderDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreateTransferOrderCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<TransferOrderDto>> Handle(CreateTransferOrderCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var transferNo = await connection.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT 'TRF-' || to_char(CURRENT_DATE, 'YYYY') || '-' || lpad(nextval('transfer_order_seq')::text, 5, '0');",
            transaction: transaction,
            cancellationToken: cancellationToken));

        var transferOrderId = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO transfer_orders (
                id, transfer_no, source_warehouse_id, destination_warehouse_id,
                status, created_at, created_by)
            VALUES (
                @Id, @TransferNo, @SourceWarehouseId, @DestinationWarehouseId,
                'DRAFT', now(), @CreatedBy);
            """,
            new
            {
                Id = transferOrderId,
                TransferNo = transferNo,
                request.Request.SourceWarehouseId,
                request.Request.DestinationWarehouseId,
                CreatedBy = _currentUser.UserId
            },
            transaction,
            cancellationToken: cancellationToken));

        foreach (var line in request.Request.Lines)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO transfer_order_lines (
                    id, transfer_order_id, item_id, batch_id,
                    source_location_id, destination_location_id, shipped_qty, received_qty)
                VALUES (
                    @Id, @TransferOrderId, @ItemId, @BatchId,
                    @SourceLocationId, @DestinationLocationId, @ShippedQty, 0);
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    TransferOrderId = transferOrderId,
                    line.ItemId,
                    line.BatchId,
                    line.SourceLocationId,
                    line.DestinationLocationId,
                    line.ShippedQty
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<TransferOrderDto>.Success(new TransferOrderDto(
            transferOrderId,
            transferNo,
            null,
            request.Request.SourceWarehouseId,
            request.Request.DestinationWarehouseId,
            "DRAFT",
            null,
            null,
            Array.Empty<TransferOrderLineDto>()));
    }
}

public sealed record ShipTransferOrderCommand(Guid TransferOrderId)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Transfers.Ship;
    public string AuditModule => "TRANSFERS";
}

public sealed class ShipTransferOrderCommandHandler : IRequestHandler<ShipTransferOrderCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public ShipTransferOrderCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(ShipTransferOrderCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var order = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Status, Guid SourceWarehouseId)>(
            new CommandDefinition(
                """
                SELECT id AS Id, status AS Status, source_warehouse_id AS SourceWarehouseId
                FROM transfer_orders
                WHERE id = @Id
                FOR UPDATE;
                """,
                new { Id = request.TransferOrderId },
                transaction,
                cancellationToken: cancellationToken));

        if (order.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Transfer.NotFound", "Transfer order not found."));
        }

        if (order.Status is "RECEIVED" or "CANCELLED")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Transfer.InvalidState", "Transfer order cannot be shipped in current state."));
        }

        var lines = (await connection.QueryAsync<(Guid ItemId, Guid? BatchId, Guid? SourceLocationId, decimal ShippedQty)>(
            new CommandDefinition(
                """
                SELECT item_id AS ItemId, batch_id AS BatchId, source_location_id AS SourceLocationId, shipped_qty AS ShippedQty
                FROM transfer_order_lines
                WHERE transfer_order_id = @TransferOrderId;
                """,
                new { TransferOrderId = request.TransferOrderId },
                transaction,
                cancellationToken: cancellationToken))).ToArray();

        foreach (var line in lines)
        {
            var locationId = line.SourceLocationId ?? order.SourceWarehouseId;

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO inventory_balances (id, item_id, location_id, batch_id, status, qty, updated_at)
                VALUES (@Id, @ItemId, @LocationId, @BatchId, 'AVAILABLE', 0, now())
                ON CONFLICT (item_id, location_id, batch_id, status) DO NOTHING;
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    line.ItemId,
                    LocationId = locationId,
                    line.BatchId
                },
                transaction,
                cancellationToken: cancellationToken));

            var affected = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE inventory_balances
                SET qty = qty - @Qty, updated_at = now()
                WHERE item_id = @ItemId
                  AND location_id = @LocationId
                  AND ((@BatchId IS NULL AND batch_id IS NULL) OR batch_id = @BatchId)
                  AND status = 'AVAILABLE'
                  AND qty >= @Qty;
                """,
                new
                {
                    line.ItemId,
                    LocationId = locationId,
                    line.BatchId,
                    Qty = line.ShippedQty
                },
                transaction,
                cancellationToken: cancellationToken));

            if (affected == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("Stock.InsufficientQuantity", "Insufficient available stock for shipping."));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO inventory_balances (id, item_id, location_id, batch_id, status, qty, updated_at)
                VALUES (@Id, @ItemId, @LocationId, @BatchId, 'IN_TRANSIT', @Qty, now())
                ON CONFLICT (item_id, location_id, batch_id, status)
                DO UPDATE SET qty = inventory_balances.qty + @Qty, updated_at = now();
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    line.ItemId,
                    LocationId = locationId,
                    line.BatchId,
                    Qty = line.ShippedQty
                },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO inventory_movements (
                    id, item_id, location_id, batch_id, movement_type, qty, direction,
                    from_status, to_status, reference_type, reference_id, performed_by, correlation_id, notes, created_at)
                VALUES (
                    @Id, @ItemId, @LocationId, @BatchId, 'TRANSFER_OUT', @Qty, 'OUT',
                    'AVAILABLE', 'IN_TRANSIT', 'TRANSFER', @ReferenceId, @PerformedBy, @CorrelationId, @Notes, now());
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    line.ItemId,
                    LocationId = locationId,
                    line.BatchId,
                    Qty = line.ShippedQty,
                    ReferenceId = request.TransferOrderId,
                    PerformedBy = _currentUser.UserId,
                    CorrelationId = _currentUser.CorrelationId,
                    Notes = "Transfer shipped"
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE transfer_orders
            SET status = 'IN_TRANSIT',
                shipped_at = now()
            WHERE id = @Id;
            """,
            new { Id = request.TransferOrderId },
            transaction,
            cancellationToken: cancellationToken));

        var outbox = OutboxMessage.Create(
            OutboxEventTypes.TransferShipped,
            "TransferOrder",
            request.TransferOrderId,
            new TransferShippedPayload(request.TransferOrderId, lines.Sum(x => x.ShippedQty)),
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
                outbox.Id,
                outbox.EventType,
                outbox.AggregateType,
                outbox.AggregateId,
                outbox.PayloadJson,
                outbox.OccurredAt,
                outbox.ProcessedAt,
                outbox.ProcessingError,
                outbox.RetryCount,
                outbox.CorrelationId
            },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(request.TransferOrderId);
    }
}

public sealed record ReceiveTransferOrderCommand(Guid TransferOrderId)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Transfers.Receive;
    public string AuditModule => "TRANSFERS";
}

public sealed class ReceiveTransferOrderCommandHandler : IRequestHandler<ReceiveTransferOrderCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public ReceiveTransferOrderCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(ReceiveTransferOrderCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var order = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Status, Guid SourceWarehouseId, Guid DestinationWarehouseId)>(
            new CommandDefinition(
                """
                SELECT id AS Id, status AS Status, source_warehouse_id AS SourceWarehouseId, destination_warehouse_id AS DestinationWarehouseId
                FROM transfer_orders
                WHERE id = @Id
                FOR UPDATE;
                """,
                new { Id = request.TransferOrderId },
                transaction,
                cancellationToken: cancellationToken));

        if (order.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Transfer.NotFound", "Transfer order not found."));
        }

        if (order.Status != "IN_TRANSIT")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Transfer.InvalidState", "Only in-transit transfers can be received."));
        }

        var lines = (await connection.QueryAsync<(Guid ItemId, Guid? BatchId, Guid? SourceLocationId, Guid? DestinationLocationId, decimal ShippedQty)>(
            new CommandDefinition(
                """
                SELECT item_id AS ItemId, batch_id AS BatchId, source_location_id AS SourceLocationId,
                       destination_location_id AS DestinationLocationId, shipped_qty AS ShippedQty
                FROM transfer_order_lines
                WHERE transfer_order_id = @TransferOrderId;
                """,
                new { TransferOrderId = request.TransferOrderId },
                transaction,
                cancellationToken: cancellationToken))).ToArray();

        foreach (var line in lines)
        {
            var sourceLocation = line.SourceLocationId ?? order.SourceWarehouseId;
            var destinationLocation = line.DestinationLocationId ?? order.DestinationWarehouseId;

            var affected = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE inventory_balances
                SET qty = qty - @Qty, updated_at = now()
                WHERE item_id = @ItemId
                  AND location_id = @LocationId
                  AND ((@BatchId IS NULL AND batch_id IS NULL) OR batch_id = @BatchId)
                  AND status = 'IN_TRANSIT'
                  AND qty >= @Qty;
                """,
                new
                {
                    line.ItemId,
                    LocationId = sourceLocation,
                    line.BatchId,
                    Qty = line.ShippedQty
                },
                transaction,
                cancellationToken: cancellationToken));

            if (affected == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("Stock.InsufficientQuantity", "Insufficient in-transit stock for receiving."));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO inventory_balances (id, item_id, location_id, batch_id, status, qty, updated_at)
                VALUES (@Id, @ItemId, @LocationId, @BatchId, 'AVAILABLE', @Qty, now())
                ON CONFLICT (item_id, location_id, batch_id, status)
                DO UPDATE SET qty = inventory_balances.qty + @Qty, updated_at = now();
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    line.ItemId,
                    LocationId = destinationLocation,
                    line.BatchId,
                    Qty = line.ShippedQty
                },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO inventory_movements (
                    id, item_id, location_id, batch_id, movement_type, qty, direction,
                    from_status, to_status, reference_type, reference_id, performed_by, correlation_id, notes, created_at)
                VALUES (
                    @Id, @ItemId, @LocationId, @BatchId, 'TRANSFER_IN', @Qty, 'IN',
                    'IN_TRANSIT', 'AVAILABLE', 'TRANSFER', @ReferenceId, @PerformedBy, @CorrelationId, @Notes, now());
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    line.ItemId,
                    LocationId = destinationLocation,
                    line.BatchId,
                    Qty = line.ShippedQty,
                    ReferenceId = request.TransferOrderId,
                    PerformedBy = _currentUser.UserId,
                    CorrelationId = _currentUser.CorrelationId,
                    Notes = "Transfer received"
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE transfer_orders
            SET status = 'RECEIVED',
                received_at = now()
            WHERE id = @Id;
            """,
            new { Id = request.TransferOrderId },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(request.TransferOrderId);
    }
}

using Dapper;

namespace AutoPartsERP.Application.Features.Inventory.AdjustInventory;

public sealed record AdjustInventoryCommand(
    Guid SkuId,
    Guid LocationId,
    Guid? BatchId,
    decimal QuantityDelta,
    string Reason,
    string? Notes,
    string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.Inventory.Adjust;
    public string AuditModule => "INVENTORY";
    public bool RequiresApproval => true;
}

public sealed class AdjustInventoryCommandValidator : AbstractValidator<AdjustInventoryCommand>
{
    public AdjustInventoryCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.SkuId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.QuantityDelta).NotEqual(0);
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3);
    }
}

public sealed class AdjustInventoryCommandHandler : IRequestHandler<AdjustInventoryCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public AdjustInventoryCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(AdjustInventoryCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var stock = await connection.QuerySingleOrDefaultAsync<(Guid Id, decimal QuantityOnHand)>(
            new CommandDefinition(
                "SELECT id AS Id, quantity_on_hand AS QuantityOnHand FROM inventory_stock WHERE sku_id = @SkuId AND location_id = @LocationId FOR UPDATE;",
                new { request.SkuId, request.LocationId },
                transaction,
                cancellationToken: cancellationToken));

        if (stock.Id == Guid.Empty)
        {
            stock = (Guid.NewGuid(), 0m);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO inventory_stock (id, sku_id, location_id, quantity_on_hand, quantity_reserved, updated_at)
                VALUES (@Id, @SkuId, @LocationId, 0, 0, now());
                """,
                new { stock.Id, request.SkuId, request.LocationId },
                transaction,
                cancellationToken: cancellationToken));
        }

        if (request.QuantityDelta < 0 && stock.QuantityOnHand + request.QuantityDelta < 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Stock.InsufficientQuantity", "Insufficient stock quantity."));
        }

        var movementId = Guid.NewGuid();
        var direction = request.QuantityDelta >= 0 ? "IN" : "OUT";
        var movementType = "ADJUSTMENT";

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE inventory_stock
            SET quantity_on_hand = quantity_on_hand + @Delta,
                updated_at = now()
            WHERE sku_id = @SkuId AND location_id = @LocationId;
            """,
            new { Delta = request.QuantityDelta, request.SkuId, request.LocationId },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO batch_movements (
                id, batch_id, movement_type, quantity, direction, reference_type, reference_id,
                from_location_id, to_location_id, unit_cost_syp, unit_cost_usd, performed_by, notes, created_at)
            VALUES (@Id, @BatchId, @MovementType, @Quantity, @Direction, 'INVENTORY_ADJUSTMENT', @ReferenceId,
                @LocationId, @LocationId, 0, 0, @PerformedBy, @Notes, now());
            """,
            new
            {
                Id = movementId,
                BatchId = request.BatchId ?? Guid.Empty,
                MovementType = movementType,
                Quantity = Math.Abs(request.QuantityDelta),
                Direction = direction,
                ReferenceId = movementId,
                request.LocationId,
                PerformedBy = _currentUser.UserId,
                request.Notes
            },
            transaction,
            cancellationToken: cancellationToken));

        var outboxMessage = OutboxMessage.Create(
            OutboxEventTypes.StockAdjusted,
            "InventoryStock",
            stock.Id,
            new StockAdjustedPayload(
                request.SkuId,
                request.LocationId,
                request.BatchId,
                request.QuantityDelta,
                request.Reason),
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
        return Result<Guid>.Success(movementId);
    }
}

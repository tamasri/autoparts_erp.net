using Dapper;
using AutoPartsERP.Domain.Extensions;

namespace AutoPartsERP.Application.Features.Inventory.ReceiveBatch;

public sealed record ReceiveBatchCommand(
    Guid SkuId,
    Guid LocationId,
    decimal Quantity,
    decimal CostPriceSyp,
    decimal CostPriceUsd,
    Guid FxRateId,
    DateOnly ReceivedDate,
    DateOnly? ExpiryDate,
    string? SupplierName,
    string? SupplierInvoice,
    string? Notes,
    string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Inventory.ManageBatches;
    public string AuditModule => "INVENTORY";
}

public sealed class ReceiveBatchCommandValidator : AbstractValidator<ReceiveBatchCommand>
{
    public ReceiveBatchCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.SkuId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.CostPriceSyp).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CostPriceUsd).GreaterThanOrEqualTo(0);
        RuleFor(x => x.FxRateId).NotEmpty();
    }
}

public sealed class ReceiveBatchCommandHandler : IRequestHandler<ReceiveBatchCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public ReceiveBatchCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(ReceiveBatchCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var sku = await connection.QuerySingleOrDefaultAsync<(bool IsBatchTracked, string Code, string Name)>(
                new CommandDefinition(
                    "SELECT is_batch_tracked AS IsBatchTracked, code AS Code, name AS Name FROM skus WHERE id = @SkuId;",
                    new { request.SkuId },
                    transaction,
                    cancellationToken: cancellationToken));

            if (string.IsNullOrWhiteSpace(sku.Code))
            {
                return Result<Guid>.Failure(new Error("Sku.NotFound", "SKU was not found."));
            }

            if (!sku.IsBatchTracked)
            {
                return Result<Guid>.Failure(new Error("Batch.NotTracked", "SKU is not batch tracked."));
            }

            var locationExists = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(1) FROM locations WHERE id = @LocationId;",
                    new { request.LocationId },
                    transaction,
                    cancellationToken: cancellationToken));

            if (locationExists == 0)
            {
                return Result<Guid>.Failure(new Error("Location.NotFound", "Location was not found."));
            }

            var batchId = Guid.NewGuid();
            var batchNumber = $"BATCH-{DateTime.UtcNow:yyyy}-{batchId.ToString("N")[..8].ToUpperInvariant()}";

            var batch = Batch.Create(
                batchNumber,
                request.SkuId,
                request.LocationId,
                request.Quantity,
                request.CostPriceSyp,
                request.CostPriceUsd,
                request.FxRateId,
                request.ReceivedDate,
                _currentUser.UserId);

            if (batch.IsFailure || batch.Value is null)
            {
                return Result<Guid>.Failure(batch.Error);
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO batches (
                        id, batch_number, sku_id, location_id, quantity_initial, quantity_current,
                        cost_price_syp, cost_price_usd, fx_rate_id, supplier_name, supplier_invoice,
                        received_date, expiry_date, status, notes, created_at, created_by)
                    VALUES (
                        @Id, @BatchNumber, @SkuId, @LocationId, @QuantityInitial, @QuantityCurrent,
                        @CostPriceSyp, @CostPriceUsd, @FxRateId, @SupplierName, @SupplierInvoice,
                        @ReceivedDate, @ExpiryDate, @Status, @Notes, @CreatedAt, @CreatedBy);
                    """,
                    new
                    {
                        Id = batchId,
                        batch.Value.BatchNumber,
                        batch.Value.SkuId,
                        batch.Value.LocationId,
                        QuantityInitial = batch.Value.QuantityInitial,
                        QuantityCurrent = batch.Value.QuantityCurrent,
                        batch.Value.CostPriceSyp,
                        batch.Value.CostPriceUsd,
                        batch.Value.FxRateId,
                        request.SupplierName,
                        request.SupplierInvoice,
                        ReceivedDate = request.ReceivedDate,
                        ExpiryDate = request.ExpiryDate,
                        Status = batch.Value.Status.ToString().ToUpperInvariant(),
                        Notes = request.Notes,
                        CreatedAt = DateTimeOffset.UtcNow,
                        CreatedBy = _currentUser.UserId
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO inventory_stock (id, sku_id, location_id, quantity_on_hand, quantity_reserved, updated_at)
                    VALUES (@Id, @SkuId, @LocationId, @Quantity, 0, now())
                    ON CONFLICT (sku_id, location_id)
                    DO UPDATE SET quantity_on_hand = inventory_stock.quantity_on_hand + EXCLUDED.quantity_on_hand,
                                  updated_at = now();
                    """,
                    new { Id = Guid.NewGuid(), request.SkuId, request.LocationId, request.Quantity },
                    transaction,
                    cancellationToken: cancellationToken));

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO batch_movements (
                        id, batch_id, movement_type, quantity, direction, reference_type, reference_id,
                        from_location_id, to_location_id, unit_cost_syp, unit_cost_usd, performed_by, notes, created_at)
                    VALUES (@Id, @BatchId, 'RECEIPT', @Quantity, 'IN', 'BATCH_RECEIPT', @ReferenceId,
                        NULL, @LocationId, @UnitCostSyp, @UnitCostUsd, @PerformedBy, @Notes, now());
                    """,
                    new
                    {
                        Id = Guid.NewGuid(),
                        BatchId = batchId,
                        request.Quantity,
                        ReferenceId = batchId,
                        request.LocationId,
                        UnitCostSyp = request.CostPriceSyp,
                        UnitCostUsd = request.CostPriceUsd,
                        PerformedBy = _currentUser.UserId,
                        request.Notes
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            var outboxMessage = OutboxMessage.Create(
                OutboxEventTypes.BatchReceived,
                "Batch",
                batchId,
                new BatchReceivedPayload(
                    batchId,
                    request.SkuId,
                    request.LocationId,
                    request.Quantity),
                _currentUser.CorrelationId);

            await connection.ExecuteAsync(
                new CommandDefinition(
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
            return Result<Guid>.Success(batchId);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}

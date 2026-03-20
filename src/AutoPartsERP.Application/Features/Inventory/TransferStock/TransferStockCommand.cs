using Dapper;

namespace AutoPartsERP.Application.Features.Inventory.TransferStock;

public sealed record TransferStockCommand(
    Guid SkuId,
    Guid FromLocationId,
    Guid ToLocationId,
    Guid? BatchId,
    decimal Quantity,
    string? Notes,
    string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Inventory.Transfer;
    public string AuditModule => "INVENTORY";
}

public sealed class TransferStockCommandValidator : AbstractValidator<TransferStockCommand>
{
    public TransferStockCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.SkuId).NotEmpty();
        RuleFor(x => x.FromLocationId).NotEmpty();
        RuleFor(x => x.ToLocationId).NotEmpty().NotEqual(x => x.FromLocationId);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class TransferStockCommandHandler : IRequestHandler<TransferStockCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public TransferStockCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(TransferStockCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var sourceStock = await connection.QuerySingleOrDefaultAsync<(Guid Id, decimal QuantityOnHand)>(
            new CommandDefinition(
                "SELECT id AS Id, quantity_on_hand AS QuantityOnHand FROM inventory_stock WHERE sku_id = @SkuId AND location_id = @LocationId FOR UPDATE;",
                new { request.SkuId, LocationId = request.FromLocationId },
                transaction,
                cancellationToken: cancellationToken));

        if (sourceStock.Id == Guid.Empty || sourceStock.QuantityOnHand < request.Quantity)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Stock.InsufficientQuantity", "Insufficient stock quantity."));
        }

        var destinationStock = await connection.QuerySingleOrDefaultAsync<(Guid Id, decimal QuantityOnHand)>(
            new CommandDefinition(
                "SELECT id AS Id, quantity_on_hand AS QuantityOnHand FROM inventory_stock WHERE sku_id = @SkuId AND location_id = @LocationId FOR UPDATE;",
                new { request.SkuId, LocationId = request.ToLocationId },
                transaction,
                cancellationToken: cancellationToken));

        if (destinationStock.Id == Guid.Empty)
        {
            destinationStock = (Guid.NewGuid(), 0m);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO inventory_stock (id, sku_id, location_id, quantity_on_hand, quantity_reserved, updated_at)
                VALUES (@Id, @SkuId, @LocationId, 0, 0, now());
                """,
                new { destinationStock.Id, request.SkuId, LocationId = request.ToLocationId },
                transaction,
                cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE inventory_stock
            SET quantity_on_hand = quantity_on_hand - @Quantity,
                updated_at = now()
            WHERE sku_id = @SkuId AND location_id = @LocationId;
            """,
            new { request.Quantity, request.SkuId, LocationId = request.FromLocationId },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE inventory_stock
            SET quantity_on_hand = quantity_on_hand + @Quantity,
                updated_at = now()
            WHERE sku_id = @SkuId AND location_id = @LocationId;
            """,
            new { request.Quantity, request.SkuId, LocationId = request.ToLocationId },
            transaction,
            cancellationToken: cancellationToken));

        var transferId = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO batch_movements (
                id, batch_id, movement_type, quantity, direction, reference_type, reference_id,
                from_location_id, to_location_id, unit_cost_syp, unit_cost_usd, performed_by, notes, created_at)
            VALUES (@IdOut, @BatchId, 'TRANSFER_OUT', @Quantity, 'OUT', 'TRANSFER', @ReferenceId,
                @FromLocationId, @ToLocationId, 0, 0, @PerformedBy, @Notes, now()),
                   (@IdIn, @BatchId, 'TRANSFER_IN', @Quantity, 'IN', 'TRANSFER', @ReferenceId,
                @FromLocationId, @ToLocationId, 0, 0, @PerformedBy, @Notes, now());
            """,
            new
            {
                IdOut = Guid.NewGuid(),
                IdIn = Guid.NewGuid(),
                BatchId = request.BatchId,
                request.Quantity,
                ReferenceId = transferId,
                request.FromLocationId,
                request.ToLocationId,
                PerformedBy = _currentUser.UserId,
                request.Notes
            },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(transferId);
    }
}

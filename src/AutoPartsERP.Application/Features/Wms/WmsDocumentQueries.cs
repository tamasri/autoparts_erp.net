using AutoPartsERP.Contracts.Wms;

namespace AutoPartsERP.Application.Features.Wms;

// Header + lines of a transfer order and a stock adjustment, so every warehouse document can be viewed and printed.

public sealed record GetTransferOrderByIdQuery(Guid TransferOrderId)
    : IRequest<Result<TransferOrderDetailDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Transfers.Read;
}

public sealed class GetTransferOrderByIdQueryHandler : IRequestHandler<GetTransferOrderByIdQuery, Result<TransferOrderDetailDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetTransferOrderByIdQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private sealed record Header(Guid Id, string TransferNo, Guid SourceWarehouseId, Guid DestinationWarehouseId, string Status, DateTimeOffset? ShippedAt, DateTimeOffset? ReceivedAt, DateTimeOffset CreatedAt);

    public async Task<Result<TransferOrderDetailDto>> Handle(GetTransferOrderByIdQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var header = await connection.QuerySingleOrDefaultAsync<Header>(new CommandDefinition(
            """
            SELECT id AS Id, transfer_no AS TransferNo, source_warehouse_id AS SourceWarehouseId, destination_warehouse_id AS DestinationWarehouseId,
                   status AS Status, shipped_at AS ShippedAt, received_at AS ReceivedAt, created_at AS CreatedAt
            FROM transfer_orders WHERE id = @Id;
            """,
            new { Id = request.TransferOrderId }, cancellationToken: cancellationToken));
        if (header is null)
        {
            return Result<TransferOrderDetailDto>.Failure(new Error("Transfer.NotFound", "Transfer order was not found."));
        }

        var lines = (await connection.QueryAsync<TransferLineViewDto>(new CommandDefinition(
            """
            SELECT l.id AS Id, l.item_id AS ItemId, i.part_number AS ItemCode, i.name_ar AS ItemName,
                   l.source_location_id AS SourceLocationId, l.destination_location_id AS DestinationLocationId,
                   l.shipped_qty AS ShippedQty, l.received_qty AS ReceivedQty
            FROM transfer_order_lines l
            INNER JOIN items i ON i.id = l.item_id
            WHERE l.transfer_order_id = @Id
            ORDER BY i.part_number;
            """,
            new { Id = request.TransferOrderId }, cancellationToken: cancellationToken))).ToArray();

        return Result<TransferOrderDetailDto>.Success(new TransferOrderDetailDto(
            header.Id, header.TransferNo, header.SourceWarehouseId, header.DestinationWarehouseId, header.Status, header.ShippedAt, header.ReceivedAt, header.CreatedAt, lines));
    }
}

public sealed record GetStockAdjustmentByIdQuery(Guid AdjustmentId)
    : IRequest<Result<StockAdjustmentDetailDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.StockAdjustments.Read;
}

public sealed class GetStockAdjustmentByIdQueryHandler : IRequestHandler<GetStockAdjustmentByIdQuery, Result<StockAdjustmentDetailDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetStockAdjustmentByIdQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private sealed record Header(Guid Id, string AdjustmentNo, string AdjustmentType, Guid WarehouseId, string ReasonCode, string Status, DateTimeOffset? PostedAt, DateTimeOffset CreatedAt);

    public async Task<Result<StockAdjustmentDetailDto>> Handle(GetStockAdjustmentByIdQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var header = await connection.QuerySingleOrDefaultAsync<Header>(new CommandDefinition(
            """
            SELECT id AS Id, adjustment_no AS AdjustmentNo, adjustment_type AS AdjustmentType, warehouse_id AS WarehouseId,
                   reason_code AS ReasonCode, status AS Status, posted_at AS PostedAt, created_at AS CreatedAt
            FROM stock_adjustments WHERE id = @Id;
            """,
            new { Id = request.AdjustmentId }, cancellationToken: cancellationToken));
        if (header is null)
        {
            return Result<StockAdjustmentDetailDto>.Failure(new Error("Adjustment.NotFound", "Stock adjustment was not found."));
        }

        var lines = (await connection.QueryAsync<StockAdjustmentLineViewDto>(new CommandDefinition(
            """
            SELECT l.id AS Id, l.item_id AS ItemId, i.part_number AS ItemCode, i.name_ar AS ItemName, l.location_id AS LocationId,
                   l.qty_delta AS QtyDelta, l.system_qty_before AS SystemQtyBefore, l.system_qty_after AS SystemQtyAfter, l.notes AS Notes
            FROM stock_adjustment_lines l
            INNER JOIN items i ON i.id = l.item_id
            WHERE l.stock_adjustment_id = @Id
            ORDER BY i.part_number;
            """,
            new { Id = request.AdjustmentId }, cancellationToken: cancellationToken))).ToArray();

        return Result<StockAdjustmentDetailDto>.Success(new StockAdjustmentDetailDto(
            header.Id, header.AdjustmentNo, header.AdjustmentType, header.WarehouseId, header.ReasonCode, header.Status, header.PostedAt, header.CreatedAt, lines));
    }
}

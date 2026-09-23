using AutoPartsERP.Application.Features.CycleCounts;
using AutoPartsERP.Application.Features.Inventory.AdjustInventory;
using AutoPartsERP.Application.Features.Inventory.ReceiveBatch;
using AutoPartsERP.Application.Features.Inventory.TransferStock;
using AutoPartsERP.Application.Features.IssueOrders;
using AutoPartsERP.Application.Features.Receiving;
using AutoPartsERP.Application.Features.StockAdjustments;
using AutoPartsERP.Application.Features.Transfers;

namespace AutoPartsERP.Application.Features.Wms;

/// <summary>
/// Which locations each warehouse request touches, and whether a user may see them (their assigned warehouses and everything under them,
/// SQL function <c>user_visible_locations</c>). A transfer is allowed when either side is the user's; shipping belongs to the source,
/// receiving to the destination.
/// </summary>
public sealed class WarehouseScope : IWarehouseScope
{
    private readonly IDbConnectionFactory _connectionFactory;

    public WarehouseScope(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<WarehouseTouch?> ResolveAsync(IWarehouseScopedRequest request, CancellationToken cancellationToken = default)
    {
        static WarehouseTouch All(params Guid[] ids) => new(ids, false);
        static WarehouseTouch Any(params Guid[] ids) => new(ids, true);

        switch (request)
        {
            case CreateReceivingDocumentCommand c: return All(c.Request.WarehouseId);
            case CreateTransferRequestCommand c: return Any(c.SourceWarehouseId, c.DestinationWarehouseId);
            case CreateTransferOrderCommand c: return Any(c.Request.SourceWarehouseId, c.Request.DestinationWarehouseId);
            case TransferStockCommand c: return Any(c.FromLocationId, c.ToLocationId);
            case CreateIssueOrderCommand c: return All(c.WarehouseId);
            case CreateCycleCountPlanCommand c: return All(c.Request.WarehouseId);
            case CreateStockAdjustmentCommand c: return All(c.Request.WarehouseId);
            case AdjustInventoryCommand c: return All(c.LocationId);
            case ReceiveBatchCommand c: return All(c.LocationId);
        }

        var (sql, id, any) = request switch
        {
            AddReceivingLineCommand c => ("SELECT warehouse_id FROM receiving_documents WHERE id = @id;", c.ReceivingDocumentId, false),
            PostReceivingDocumentCommand c => ("SELECT warehouse_id FROM receiving_documents WHERE id = @id;", c.ReceivingDocumentId, false),
            GetPutawayTasksQuery c => ("SELECT warehouse_id FROM receiving_documents WHERE id = @id;", c.ReceivingDocumentId, false),
            GetReceivingDocumentDetailQuery c => ("SELECT warehouse_id FROM receiving_documents WHERE id = @id;", c.DocumentId, false),
            CompletePutawayTaskCommand c => (
                """
                SELECT d.warehouse_id FROM putaway_tasks t
                INNER JOIN receiving_lines l ON l.id = t.receiving_line_id
                INNER JOIN receiving_documents d ON d.id = l.receiving_document_id
                WHERE t.id = @id;
                """, c.TaskId, false),
            ShipTransferOrderCommand c => ("SELECT source_warehouse_id FROM transfer_orders WHERE id = @id;", c.TransferOrderId, false),
            ReceiveTransferOrderCommand c => ("SELECT destination_warehouse_id FROM transfer_orders WHERE id = @id;", c.TransferOrderId, false),
            GetTransferOrderByIdQuery c => ("SELECT unnest(ARRAY[source_warehouse_id, destination_warehouse_id]) FROM transfer_orders WHERE id = @id;", c.TransferOrderId, true),
            GeneratePickTasksCommand c => ("SELECT warehouse_id FROM issue_orders WHERE id = @id;", c.IssueOrderId, false),
            CompletePickTaskCommand c => ("SELECT warehouse_id FROM issue_orders WHERE id = @id;", c.IssueOrderId, false),
            VerifyPickTaskCommand c => ("SELECT warehouse_id FROM issue_orders WHERE id = @id;", c.IssueOrderId, false),
            IssueOrderCommand c => ("SELECT warehouse_id FROM issue_orders WHERE id = @id;", c.IssueOrderId, false),
            GetIssueOrderByIdQuery c => ("SELECT warehouse_id FROM issue_orders WHERE id = @id;", c.IssueOrderId, false),
            RecordCycleCountCommand c => ("SELECT warehouse_id FROM cycle_count_plans WHERE id = @id;", c.Request.CycleCountPlanId, false),
            ApproveCycleCountVarianceCommand c => ("SELECT warehouse_id FROM cycle_count_plans WHERE id = @id;", c.CycleCountPlanId, false),
            GetCycleCountPlanByIdQuery c => ("SELECT warehouse_id FROM cycle_count_plans WHERE id = @id;", c.PlanId, false),
            PostStockAdjustmentCommand c => ("SELECT warehouse_id FROM stock_adjustments WHERE id = @id;", c.StockAdjustmentId, false),
            GetStockAdjustmentByIdQuery c => ("SELECT warehouse_id FROM stock_adjustments WHERE id = @id;", c.AdjustmentId, false),
            _ => throw new InvalidOperationException($"{request.GetType().Name} is warehouse-scoped but WarehouseScope does not know which warehouse it touches.")
        };

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var ids = (await connection.QueryAsync<Guid>(new CommandDefinition(sql, new { id }, cancellationToken: cancellationToken))).ToArray();
        return ids.Length == 0 ? null : new WarehouseTouch(ids, any);
    }

    public async Task<bool> CanSeeAsync(Guid userId, WarehouseTouch touch, CancellationToken cancellationToken = default)
    {
        var ids = touch.Locations.Distinct().ToArray();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var visible = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM user_visible_locations(@userId) v WHERE v.location_id = ANY(@ids);",
            new { userId, ids }, cancellationToken: cancellationToken));
        return touch.AnyIsEnough ? visible > 0 : visible == ids.Length;
    }
}

/// <summary>
/// The same rule in list queries: each one filters with <c>(@ScopeAll OR column IN (SELECT location_id FROM user_visible_locations(@ScopeUser)))</c>
/// and passes <c>ScopeAll = SeesAll(user)</c>, <c>ScopeUser = user.UserId</c>.
/// </summary>
public static class WarehouseScopeSql
{
    public static bool SeesAll(ICurrentUser user) => user.HasPermission(PermissionCodes.Inventory.AllWarehouses);
}

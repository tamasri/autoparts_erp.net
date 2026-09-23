using AutoPartsERP.Contracts.Wms;

namespace AutoPartsERP.Application.Features.Wms;

// Read models for the warehouse screens. Typed on purpose: rows read as dynamic come back with lower-cased keys (the UI
// looked for camelCase names and found nothing), and the old list handlers reported the size of the *page* as the total.

public sealed record GetIssueOrderByIdQuery(Guid IssueOrderId)
    : IRequest<Result<IssueOrderDetailDto>>, IAuthorizedRequest, IWarehouseScopedRequest
{
    public string RequiredPermission => PermissionCodes.IssueOrders.Read;
}

public sealed class GetIssueOrderByIdQueryHandler : IRequestHandler<GetIssueOrderByIdQuery, Result<IssueOrderDetailDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetIssueOrderByIdQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<IssueOrderDetailDto>> Handle(GetIssueOrderByIdQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var order = await connection.QuerySingleOrDefaultAsync<IssueOrderListDto>(new CommandDefinition(
            """
            SELECT id AS Id, order_no AS OrderNo, source_type AS SourceType, source_id AS SourceId, warehouse_id AS WarehouseId,
                   status AS Status, issued_at AS IssuedAt, created_at AS CreatedAt
            FROM issue_orders WHERE id = @IssueOrderId;
            """,
            new { request.IssueOrderId },
            cancellationToken: cancellationToken));
        if (order is null)
        {
            return Result<IssueOrderDetailDto>.Failure(new Error("IssueOrder.NotFound", "Issue order was not found."));
        }

        var lines = (await connection.QueryAsync<IssueOrderLineViewDto>(new CommandDefinition(
            """
            SELECT l.id AS Id, l.item_id AS ItemId, i.part_number AS ItemCode, i.name_ar AS ItemName,
                   l.requested_qty AS RequestedQty, l.picked_qty AS PickedQty, l.verified_qty AS VerifiedQty,
                   l.issued_qty AS IssuedQty, l.source_location_id AS SourceLocationId
            FROM issue_order_lines l
            INNER JOIN items i ON i.id = l.item_id
            WHERE l.issue_order_id = @IssueOrderId
            ORDER BY i.part_number;
            """,
            new { request.IssueOrderId },
            cancellationToken: cancellationToken))).ToArray();

        var tasks = (await connection.QueryAsync<PickTaskViewDto>(new CommandDefinition(
            """
            SELECT t.id AS Id, t.issue_order_line_id AS IssueOrderLineId, i.part_number AS ItemCode, i.name_ar AS ItemName,
                   t.location_id AS LocationId, lo.code AS LocationCode, t.qty AS Qty, t.status AS Status
            FROM pick_tasks t
            INNER JOIN issue_order_lines l ON l.id = t.issue_order_line_id
            INNER JOIN items i ON i.id = l.item_id
            INNER JOIN locations lo ON lo.id = t.location_id
            WHERE l.issue_order_id = @IssueOrderId
            ORDER BY t.pick_sequence;
            """,
            new { request.IssueOrderId },
            cancellationToken: cancellationToken))).ToArray();

        return Result<IssueOrderDetailDto>.Success(new IssueOrderDetailDto(order, lines, tasks));
    }
}

public sealed record GetCycleCountPlanByIdQuery(Guid PlanId)
    : IRequest<Result<CycleCountPlanDetailDto>>, IAuthorizedRequest, IWarehouseScopedRequest
{
    public string RequiredPermission => PermissionCodes.CycleCounts.Read;
}

public sealed class GetCycleCountPlanByIdQueryHandler : IRequestHandler<GetCycleCountPlanByIdQuery, Result<CycleCountPlanDetailDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetCycleCountPlanByIdQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<CycleCountPlanDetailDto>> Handle(GetCycleCountPlanByIdQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var plan = await connection.QuerySingleOrDefaultAsync<PlanRow>(new CommandDefinition(
            "SELECT id AS Id, warehouse_id AS WarehouseId, scope_type AS ScopeType, status AS Status, scheduled_for AS ScheduledFor FROM cycle_count_plans WHERE id = @PlanId;",
            new { request.PlanId },
            cancellationToken: cancellationToken));
        if (plan is null)
        {
            return Result<CycleCountPlanDetailDto>.Failure(new Error("CycleCount.NotFound", "Cycle count plan was not found."));
        }

        var lines = (await connection.QueryAsync<CycleCountLineViewDto>(new CommandDefinition(
            """
            SELECT l.id AS Id, l.item_id AS ItemId, i.part_number AS ItemCode, i.name_ar AS ItemName,
                   l.location_id AS LocationId, lo.code AS LocationCode, l.system_qty AS SystemQty,
                   l.counted_qty AS CountedQty, l.variance_qty AS VarianceQty, l.reason_code AS ReasonCode, l.notes AS Notes
            FROM cycle_count_lines l
            INNER JOIN items i ON i.id = l.item_id
            INNER JOIN locations lo ON lo.id = l.location_id
            WHERE l.cycle_count_plan_id = @PlanId
            ORDER BY lo.code, i.part_number;
            """,
            new { request.PlanId },
            cancellationToken: cancellationToken))).ToArray();

        return Result<CycleCountPlanDetailDto>.Success(
            new CycleCountPlanDetailDto(plan.Id, plan.WarehouseId, plan.ScopeType, plan.Status, plan.ScheduledFor, lines));
    }

    private sealed record PlanRow(Guid Id, Guid WarehouseId, string ScopeType, string Status, DateOnly ScheduledFor);
}

public sealed record GetReceivingDocumentDetailQuery(Guid DocumentId)
    : IRequest<Result<ReceivingDocumentDetailDto>>, IAuthorizedRequest, IWarehouseScopedRequest
{
    public string RequiredPermission => PermissionCodes.Receiving.Read;
}

public sealed class GetReceivingDocumentDetailQueryHandler : IRequestHandler<GetReceivingDocumentDetailQuery, Result<ReceivingDocumentDetailDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetReceivingDocumentDetailQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<ReceivingDocumentDetailDto>> Handle(GetReceivingDocumentDetailQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var header = await connection.QuerySingleOrDefaultAsync<HeaderRow>(new CommandDefinition(
            """
            SELECT id AS Id, document_no AS DocumentNo, vendor_party_id AS VendorPartyId, purchase_order_ref AS PurchaseOrderRef,
                   warehouse_id AS WarehouseId, status AS Status, posted_at AS PostedAt, notes AS Notes
            FROM receiving_documents WHERE id = @DocumentId;
            """,
            new { request.DocumentId },
            cancellationToken: cancellationToken));
        if (header is null)
        {
            return Result<ReceivingDocumentDetailDto>.Failure(new Error("Receiving.NotFound", "Receiving document was not found."));
        }

        var lines = (await connection.QueryAsync<ReceivingLineViewDto>(new CommandDefinition(
            """
            SELECT l.id AS Id, l.item_id AS ItemId, i.part_number AS ItemCode, i.name_ar AS ItemName,
                   l.expected_qty AS ExpectedQty, l.received_qty AS ReceivedQty, l.rejected_qty AS RejectedQty,
                   l.assigned_location_id AS AssignedLocationId, l.condition_status AS ConditionStatus
            FROM receiving_lines l
            INNER JOIN items i ON i.id = l.item_id
            WHERE l.receiving_document_id = @DocumentId
            ORDER BY l.created_at;
            """,
            new { request.DocumentId },
            cancellationToken: cancellationToken))).ToArray();

        return Result<ReceivingDocumentDetailDto>.Success(new ReceivingDocumentDetailDto(
            header.Id, header.DocumentNo, header.VendorPartyId, header.PurchaseOrderRef, header.WarehouseId, header.Status,
            header.PostedAt, header.Notes, lines));
    }

    private sealed record HeaderRow(
        Guid Id, string DocumentNo, Guid? VendorPartyId, string? PurchaseOrderRef, Guid WarehouseId, string Status,
        DateTimeOffset? PostedAt, string? Notes);
}

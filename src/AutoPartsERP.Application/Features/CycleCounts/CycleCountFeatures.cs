namespace AutoPartsERP.Application.Features.CycleCounts;

public sealed record GetCycleCountPlansQuery(int PageNumber = 1, int PageSize = 20)
    : IRequest<Result<PagedResponse<CycleCountPlanDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.CycleCounts.Read;
}

public sealed class GetCycleCountPlansQueryHandler : IRequestHandler<GetCycleCountPlansQuery, Result<PagedResponse<CycleCountPlanDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetCycleCountPlansQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<CycleCountPlanDto>>> Handle(GetCycleCountPlansQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = request.PageSize <= 0 ? 20 : Math.Min(request.PageSize, 100);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var rows = (await connection.QueryAsync<PlanRow>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    warehouse_id AS WarehouseId,
                    scope_type AS ScopeType,
                    scope_filter::text AS ScopeFilterJson,
                    status AS Status,
                    scheduled_for AS ScheduledFor,
                    COUNT(*) OVER() AS TotalCount
                FROM cycle_count_plans
                ORDER BY created_at DESC
                OFFSET @Offset
                LIMIT @PageSize;
                """,
                new { Offset = (pageNumber - 1) * pageSize, PageSize = pageSize },
                cancellationToken: cancellationToken))).ToArray();

        var items = rows.Select(x => new CycleCountPlanDto(
            x.Id,
            x.WarehouseId,
            x.ScopeType,
            x.ScopeFilterJson,
            x.Status,
            x.ScheduledFor,
            Array.Empty<CycleCountLineDto>())).ToArray();

        var total = rows.Length == 0 ? 0 : rows[0].TotalCount;
        return Result<PagedResponse<CycleCountPlanDto>>.Success(new PagedResponse<CycleCountPlanDto>(items, pageNumber, pageSize, total));
    }

    private sealed record PlanRow(
        Guid Id,
        Guid WarehouseId,
        string ScopeType,
        string? ScopeFilterJson,
        string Status,
        DateOnly ScheduledFor,
        long TotalCount);
}

public sealed record CreateCycleCountPlanCommand(CreateCycleCountPlanRequest Request)
    : IRequest<Result<CycleCountPlanDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.CycleCounts.Create;
    public string AuditModule => "CYCLE_COUNTS";
}

public sealed class CreateCycleCountPlanCommandValidator : AbstractValidator<CreateCycleCountPlanCommand>
{
    public CreateCycleCountPlanCommandValidator()
    {
        RuleFor(x => x.Request.WarehouseId).NotEmpty();
        RuleFor(x => x.Request.ScopeType).NotEmpty().MaximumLength(24);
    }
}

public sealed class CreateCycleCountPlanCommandHandler : IRequestHandler<CreateCycleCountPlanCommand, Result<CycleCountPlanDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreateCycleCountPlanCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<CycleCountPlanDto>> Handle(CreateCycleCountPlanCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var planId = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO cycle_count_plans (
                id, warehouse_id, scope_type, scope_filter, status, scheduled_for, created_by, created_at)
            VALUES (
                @Id, @WarehouseId, @ScopeType, CAST(@ScopeFilter AS jsonb), 'DRAFT', @ScheduledFor, @CreatedBy, now());
            """,
            new
            {
                Id = planId,
                request.Request.WarehouseId,
                ScopeType = request.Request.ScopeType.Trim().ToUpperInvariant(),
                ScopeFilter = string.IsNullOrWhiteSpace(request.Request.ScopeFilterJson) ? "{}" : request.Request.ScopeFilterJson,
                request.Request.ScheduledFor,
                CreatedBy = _currentUser.UserId
            },
            transaction,
            cancellationToken: cancellationToken));

        var stocks = await connection.QueryAsync<(Guid ItemId, Guid LocationId, decimal Qty)>(
            new CommandDefinition(
                """
                SELECT item_id AS ItemId, location_id AS LocationId, SUM(qty) AS Qty
                FROM inventory_balances
                WHERE location_id = @WarehouseId
                  AND status = 'AVAILABLE'
                GROUP BY item_id, location_id;
                """,
                new { WarehouseId = request.Request.WarehouseId },
                transaction,
                cancellationToken: cancellationToken));

        foreach (var stock in stocks)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO cycle_count_lines (
                    id, cycle_count_plan_id, item_id, location_id, system_qty,
                    counted_qty, variance_qty, reason_code, notes)
                VALUES (
                    @Id, @PlanId, @ItemId, @LocationId, @SystemQty,
                    NULL, 0, NULL, NULL);
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    PlanId = planId,
                    stock.ItemId,
                    stock.LocationId,
                    SystemQty = stock.Qty
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<CycleCountPlanDto>.Success(new CycleCountPlanDto(
            planId,
            request.Request.WarehouseId,
            request.Request.ScopeType.Trim().ToUpperInvariant(),
            request.Request.ScopeFilterJson,
            "DRAFT",
            request.Request.ScheduledFor,
            Array.Empty<CycleCountLineDto>()));
    }
}

public sealed record RecordCycleCountCommand(RecordCycleCountRequest Request)
    : IRequest<Result>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.CycleCounts.Record;
    public string AuditModule => "CYCLE_COUNTS";
}

public sealed class RecordCycleCountCommandValidator : AbstractValidator<RecordCycleCountCommand>
{
    public RecordCycleCountCommandValidator()
    {
        RuleFor(x => x.Request.CycleCountPlanId).NotEmpty();
        RuleFor(x => x.Request.Lines).NotEmpty();
    }
}

public sealed class RecordCycleCountCommandHandler : IRequestHandler<RecordCycleCountCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public RecordCycleCountCommandHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result> Handle(RecordCycleCountCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var line in request.Request.Lines)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE cycle_count_lines
                SET counted_qty = @CountedQty,
                    variance_qty = @CountedQty - system_qty
                WHERE id = @LineId
                  AND cycle_count_plan_id = @PlanId;
                """,
                new
                {
                    LineId = line.LineId,
                    PlanId = request.Request.CycleCountPlanId,
                    line.CountedQty
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE cycle_count_plans
            SET status = 'PENDING_APPROVAL'
            WHERE id = @PlanId;
            """,
            new { PlanId = request.Request.CycleCountPlanId },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record ApproveCycleCountVarianceCommand(Guid CycleCountPlanId)
    : IRequest<Result>, IAuthorizedRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.CycleCounts.ApproveVariance;
    public string AuditModule => "CYCLE_COUNTS";
    public bool RequiresApproval => true;
}

public sealed class ApproveCycleCountVarianceCommandHandler : IRequestHandler<ApproveCycleCountVarianceCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public ApproveCycleCountVarianceCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(ApproveCycleCountVarianceCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var header = await connection.QuerySingleOrDefaultAsync<(Guid Id, Guid WarehouseId)>(
            new CommandDefinition(
                "SELECT id AS Id, warehouse_id AS WarehouseId FROM cycle_count_plans WHERE id = @Id FOR UPDATE;",
                new { Id = request.CycleCountPlanId },
                transaction,
                cancellationToken: cancellationToken));

        if (header.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure(new Error("CycleCount.NotFound", "Cycle count plan was not found."));
        }

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
                @Id, @AdjustmentNo, 'CYCLE_COUNT', @WarehouseId, 'CYCLE_COUNT', 'DRAFT', now(), @CreatedBy);
            """,
            new
            {
                Id = adjustmentId,
                AdjustmentNo = adjustmentNo,
                header.WarehouseId,
                CreatedBy = _currentUser.UserId
            },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO stock_adjustment_lines (
                id, stock_adjustment_id, item_id, location_id, batch_id, status,
                qty_delta, system_qty_before, system_qty_after, notes)
            SELECT
                uuid_generate_v4(),
                @StockAdjustmentId,
                item_id,
                location_id,
                NULL,
                'AVAILABLE',
                variance_qty,
                system_qty,
                system_qty + variance_qty,
                notes
            FROM cycle_count_lines
            WHERE cycle_count_plan_id = @PlanId
              AND variance_qty <> 0;
            """,
            new
            {
                StockAdjustmentId = adjustmentId,
                PlanId = request.CycleCountPlanId
            },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE cycle_count_plans SET status = 'POSTED', posted_by = @PostedBy, posted_at = now() WHERE id = @Id;",
            new { Id = request.CycleCountPlanId, PostedBy = _currentUser.UserId },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}

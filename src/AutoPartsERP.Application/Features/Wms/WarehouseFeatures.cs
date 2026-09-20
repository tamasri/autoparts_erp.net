using AutoPartsERP.Contracts.Wms;

namespace AutoPartsERP.Application.Features.Wms;

// Warehouse control: the item-movement ledger, a per-location stock overview and create/edit of warehouses and locations.

public sealed record GetStockMovementsQuery(
    Guid? ItemId,
    Guid? LocationId,
    string? MovementType,
    string? Direction,
    DateOnly? From,
    DateOnly? To,
    string? Search,
    int PageNumber = 1,
    int PageSize = 50)
    : IRequest<Result<PagedResponse<StockMovementDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Inventory.Read;
}

public sealed class GetStockMovementsQueryHandler : IRequestHandler<GetStockMovementsQuery, Result<PagedResponse<StockMovementDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetStockMovementsQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<StockMovementDto>>> Handle(GetStockMovementsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(request.PageNumber, 1);
        var size = Math.Clamp(request.PageSize, 1, 500);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        // The running balance is only meaningful (and only affordable) for one item; without an item it is left empty.
        var balanceExpr = request.ItemId.HasValue
            ? "SUM(CASE WHEN m.direction = 'IN' THEN m.qty ELSE -m.qty END) OVER (PARTITION BY m.item_id, m.location_id ORDER BY m.created_at, m.id)"
            : "NULL::numeric";

        var args = new
        {
            request.ItemId,
            request.LocationId,
            MovementType = string.IsNullOrWhiteSpace(request.MovementType) ? null : request.MovementType.Trim().ToUpperInvariant(),
            Direction = string.IsNullOrWhiteSpace(request.Direction) ? null : request.Direction.Trim().ToUpperInvariant(),
            From = request.From?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            To = request.To?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Search = string.IsNullOrWhiteSpace(request.Search) ? null : $"%{request.Search.Trim()}%",
            Offset = (page - 1) * size,
            Size = size
        };

        var rows = (await connection.QueryAsync<StockMovementDto>(new CommandDefinition(
            $"""
            SELECT * FROM (
                SELECT m.id AS Id, m.created_at AS CreatedAt, m.item_id AS ItemId, i.part_number AS ItemCode, COALESCE(NULLIF(i.name_ar, ''), i.name_en) AS ItemName,
                       m.location_id AS LocationId, l.code AS LocationCode, m.movement_type AS MovementType, m.direction AS Direction,
                       m.qty AS Qty, {balanceExpr} AS BalanceAfter, m.reference_type AS ReferenceType, m.reference_id AS ReferenceId,
                       u.user_name AS PerformedBy, m.notes AS Notes
                FROM inventory_movements m
                INNER JOIN items i ON i.id = m.item_id
                INNER JOIN locations l ON l.id = m.location_id
                LEFT JOIN asp_net_users u ON u.id = m.performed_by
                WHERE (@ItemId::uuid IS NULL OR m.item_id = @ItemId)
            ) x
            WHERE (@LocationId::uuid IS NULL OR x.LocationId = @LocationId)
              AND (@MovementType::text IS NULL OR x.MovementType = @MovementType)
              AND (@Direction::text IS NULL OR x.Direction = @Direction)
              AND (@From::timestamptz IS NULL OR x.CreatedAt >= @From)
              AND (@To::timestamptz IS NULL OR x.CreatedAt < @To)
              AND (@Search::text IS NULL OR x.ItemCode ILIKE @Search OR x.ItemName ILIKE @Search OR x.Notes ILIKE @Search)
            ORDER BY x.CreatedAt DESC, x.Id
            OFFSET @Offset LIMIT @Size;
            """,
            args, cancellationToken: cancellationToken))).ToArray();

        var total = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT count(*)
            FROM inventory_movements m
            INNER JOIN items i ON i.id = m.item_id
            WHERE (@ItemId::uuid IS NULL OR m.item_id = @ItemId)
              AND (@LocationId::uuid IS NULL OR m.location_id = @LocationId)
              AND (@MovementType::text IS NULL OR m.movement_type = @MovementType)
              AND (@Direction::text IS NULL OR m.direction = @Direction)
              AND (@From::timestamptz IS NULL OR m.created_at >= @From)
              AND (@To::timestamptz IS NULL OR m.created_at < @To)
              AND (@Search::text IS NULL OR i.part_number ILIKE @Search OR i.name_ar ILIKE @Search OR i.name_en ILIKE @Search OR m.notes ILIKE @Search);
            """,
            args, cancellationToken: cancellationToken));

        return Result<PagedResponse<StockMovementDto>>.Success(new PagedResponse<StockMovementDto>(rows, page, size, total));
    }
}

public sealed record GetLocationOverviewQuery(bool IncludeInactive)
    : IRequest<Result<IReadOnlyCollection<LocationOverviewDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Inventory.Read;
}

public sealed class GetLocationOverviewQueryHandler : IRequestHandler<GetLocationOverviewQuery, Result<IReadOnlyCollection<LocationOverviewDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetLocationOverviewQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<IReadOnlyCollection<LocationOverviewDto>>> Handle(GetLocationOverviewQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<LocationOverviewDto>(new CommandDefinition(
            """
            SELECT l.id AS Id, l.code AS Code, l.name AS Name, l.type AS Type, l.parent_id AS ParentId, l.is_active AS IsActive,
                   COALESCE(s.sku_count, 0)::int AS SkuCount, COALESCE(s.qty, 0) AS TotalQty, COALESCE(s.value_usd, 0) AS ValueUsd,
                   (SELECT count(*) FROM locations c WHERE c.parent_id = l.id)::int AS ChildCount
            FROM locations l
            LEFT JOIN (
                SELECT st.location_id,
                       count(*) FILTER (WHERE st.quantity_on_hand > 0) AS sku_count,
                       SUM(st.quantity_on_hand) AS qty,
                       SUM(st.quantity_on_hand * k.cost_price_usd) AS value_usd
                FROM inventory_stock st
                INNER JOIN skus k ON k.id = st.sku_id
                GROUP BY st.location_id) s ON s.location_id = l.id
            WHERE @IncludeInactive OR l.is_active
            ORDER BY l.code;
            """,
            new { request.IncludeInactive }, cancellationToken: cancellationToken))).ToArray();
        return Result<IReadOnlyCollection<LocationOverviewDto>>.Success(rows);
    }
}

public static class LocationTypes
{
    public static readonly IReadOnlyCollection<string> All = ["WAREHOUSE", "SHELF", "VEHICLE", "RETURN", "QUARANTINE"];
}

public sealed record CreateLocationCommand(string Code, string Name, string Type, Guid? ParentId)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Inventory.Write;
    public string AuditModule => "LOCATIONS";
}

public sealed class CreateLocationCommandValidator : AbstractValidator<CreateLocationCommand>
{
    public CreateLocationCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30).Matches("^[A-Za-z0-9_-]+$").WithMessage("Code may contain letters, digits, '-' and '_' only.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Type).Must(t => LocationTypes.All.Contains((t ?? string.Empty).Trim().ToUpperInvariant())).WithMessage("Unknown location type.");
    }
}

public sealed class CreateLocationCommandHandler : IRequestHandler<CreateLocationCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreateLocationCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(CreateLocationCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var code = request.Code.Trim().ToUpperInvariant();

        if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS (SELECT 1 FROM locations WHERE upper(code) = @code);", new { code }, cancellationToken: cancellationToken)))
        {
            return Result<Guid>.Failure(new Error("Location.DuplicateCode", "A location with this code already exists."));
        }

        if (request.ParentId.HasValue && !await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM locations WHERE id = @Id AND is_active);", new { Id = request.ParentId.Value }, cancellationToken: cancellationToken)))
        {
            return Result<Guid>.Failure(new Error("Location.ParentNotFound", "The parent location does not exist or is inactive."));
        }

        var id = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO locations (id, code, name, type, parent_id, is_active, created_at, created_by) VALUES (@id, @code, @name, @type, @parentId, TRUE, now(), @by);",
            new { id, code, name = request.Name.Trim(), type = request.Type.Trim().ToUpperInvariant(), parentId = request.ParentId, by = _currentUser.UserId },
            cancellationToken: cancellationToken));
        return Result<Guid>.Success(id);
    }
}

public sealed record UpdateLocationCommand(Guid Id, string Name, string Type, Guid? ParentId, bool IsActive)
    : IRequest<Result>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Inventory.Write;
    public string AuditModule => "LOCATIONS";
}

public sealed class UpdateLocationCommandValidator : AbstractValidator<UpdateLocationCommand>
{
    public UpdateLocationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Type).Must(t => LocationTypes.All.Contains((t ?? string.Empty).Trim().ToUpperInvariant())).WithMessage("Unknown location type.");
    }
}

public sealed class UpdateLocationCommandHandler : IRequestHandler<UpdateLocationCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public UpdateLocationCommandHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result> Handle(UpdateLocationCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var current = await connection.QuerySingleOrDefaultAsync<(Guid Id, bool IsActive)>(new CommandDefinition(
            "SELECT id AS Id, is_active AS IsActive FROM locations WHERE id = @Id;", new { request.Id }, cancellationToken: cancellationToken));
        if (current.Id == Guid.Empty)
        {
            return Result.Failure(new Error("Location.NotFound", "Location was not found."));
        }

        if (request.ParentId.HasValue)
        {
            if (request.ParentId.Value == request.Id)
            {
                return Result.Failure(new Error("Location.InvalidParent", "A location cannot be its own parent."));
            }

            // Walk up from the proposed parent: reaching this location would create a cycle.
            var cycle = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                WITH RECURSIVE up AS (
                    SELECT id, parent_id FROM locations WHERE id = @Parent
                    UNION ALL
                    SELECT l.id, l.parent_id FROM locations l INNER JOIN up ON l.id = up.parent_id)
                SELECT EXISTS (SELECT 1 FROM up WHERE id = @Id);
                """,
                new { Parent = request.ParentId.Value, request.Id }, cancellationToken: cancellationToken));
            if (cycle)
            {
                return Result.Failure(new Error("Location.InvalidParent", "The chosen parent is below this location."));
            }
        }

        if (current.IsActive && !request.IsActive)
        {
            // Never park stock in an inactive location: it would disappear from every picker while still being counted.
            var stocked = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                WITH RECURSIVE down AS (
                    SELECT id FROM locations WHERE id = @Id
                    UNION ALL
                    SELECT l.id FROM locations l INNER JOIN down d ON l.parent_id = d.id)
                SELECT EXISTS (SELECT 1 FROM inventory_stock s INNER JOIN down d ON d.id = s.location_id WHERE s.quantity_on_hand > 0);
                """,
                new { request.Id }, cancellationToken: cancellationToken));
            if (stocked)
            {
                return Result.Failure(new Error("Location.HasStock", "The location (or one below it) still holds stock; move it before deactivating."));
            }
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE locations SET name = @name, type = @type, parent_id = @parentId, is_active = @isActive WHERE id = @id;",
            new { id = request.Id, name = request.Name.Trim(), type = request.Type.Trim().ToUpperInvariant(), parentId = request.ParentId, isActive = request.IsActive },
            cancellationToken: cancellationToken));
        return Result.Success();
    }
}

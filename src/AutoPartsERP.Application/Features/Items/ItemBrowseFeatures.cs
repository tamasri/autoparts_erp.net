namespace AutoPartsERP.Application.Features.Items;

/// <summary>
/// Server-paged catalogue browse. Unlike SearchItemsQuery (which requires a non-empty query and
/// is tuned for part-number lookup), this lists every item with an optional free-text filter, and
/// aggregates stock only for the rows of the requested page so it stays fast as the catalogue grows.
/// </summary>
public sealed record BrowseItemsQuery(string? Search, int PageNumber, int PageSize, bool IncludeInactive)
    : IRequest<Result<PagedResponse<ItemListDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Items.Read;
}

public sealed class BrowseItemsQueryHandler : IRequestHandler<BrowseItemsQuery, Result<PagedResponse<ItemListDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public BrowseItemsQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<ItemListDto>>> Handle(BrowseItemsQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 100);
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : $"%{request.Search.Trim()}%";

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<ItemListDto>(
            new CommandDefinition(
                """
                WITH page AS (
                    SELECT i.id, i.part_number, i.name_en, i.name_ar, i.brand, i.is_active, i.is_stop_ship,
                           i.has_warranty, i.reorder_level, COUNT(*) OVER() AS total_count
                    FROM items i
                    WHERE (@IncludeInactive OR i.is_active)
                      AND (@Search::text IS NULL
                           OR i.part_number ILIKE @Search
                           OR i.name_en ILIKE @Search
                           OR i.name_ar ILIKE @Search
                           OR i.brand ILIKE @Search)
                    ORDER BY i.part_number_canonical
                    OFFSET @Offset LIMIT @PageSize
                )
                SELECT p.id AS Id, p.part_number AS PartNumber, p.name_en AS NameEn, p.name_ar AS NameAr,
                       p.brand AS Brand, p.is_active AS IsActive, p.is_stop_ship AS IsStopShip,
                       p.has_warranty AS HasWarranty, p.reorder_level AS ReorderLevel,
                       COALESCE((SELECT SUM(ib.qty) FROM inventory_balances ib
                                 WHERE ib.item_id = p.id AND ib.status = 'AVAILABLE'), 0) AS AvailableQty,
                       p.total_count AS TotalCount
                FROM page p
                ORDER BY p.part_number;
                """,
                new { request.IncludeInactive, Search = search, Offset = (pageNumber - 1) * pageSize, PageSize = pageSize },
                cancellationToken: cancellationToken))).ToList();

        var total = rows.Count == 0 ? 0 : rows[0].TotalCount;
        return Result<PagedResponse<ItemListDto>>.Success(new PagedResponse<ItemListDto>(rows, pageNumber, pageSize, total));
    }
}

public sealed record GetItemStockQuery(Guid ItemId)
    : IRequest<Result<IReadOnlyCollection<ItemWarehouseStockDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Items.Read;
}

public sealed class GetItemStockQueryHandler : IRequestHandler<GetItemStockQuery, Result<IReadOnlyCollection<ItemWarehouseStockDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetItemStockQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<IReadOnlyCollection<ItemWarehouseStockDto>>> Handle(GetItemStockQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<StockRow>(
            new CommandDefinition(
                """
                SELECT l.code AS Warehouse,
                       COALESCE(SUM(CASE WHEN ib.status = 'AVAILABLE' THEN ib.qty ELSE 0 END), 0) AS AvailableQty,
                       COALESCE(SUM(CASE WHEN ib.status = 'RESERVED' THEN ib.qty ELSE 0 END), 0) AS ReservedQty
                FROM inventory_balances ib
                INNER JOIN locations l ON l.id = ib.location_id
                WHERE ib.item_id = @ItemId
                GROUP BY l.code
                ORDER BY l.code;
                """,
                new { request.ItemId },
                cancellationToken: cancellationToken))).ToList();

        IReadOnlyCollection<ItemWarehouseStockDto> dtos = rows
            .Select(r => new ItemWarehouseStockDto(r.Warehouse, r.AvailableQty, r.ReservedQty, null))
            .ToArray();
        return Result<IReadOnlyCollection<ItemWarehouseStockDto>>.Success(dtos);
    }

    private sealed record StockRow(string Warehouse, decimal AvailableQty, decimal ReservedQty);
}

public sealed record GetItemAliasesQuery(Guid ItemId)
    : IRequest<Result<IReadOnlyCollection<ItemAliasDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Items.Read;
}

public sealed class GetItemAliasesQueryHandler : IRequestHandler<GetItemAliasesQuery, Result<IReadOnlyCollection<ItemAliasDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetItemAliasesQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<IReadOnlyCollection<ItemAliasDto>>> Handle(GetItemAliasesQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<AliasRow>(
            new CommandDefinition(
                """
                SELECT id AS Id, item_id AS ItemId, alias AS Alias, alias_canonical AS AliasCanonical,
                       source AS Source, created_at AS CreatedAt
                FROM item_aliases
                WHERE item_id = @ItemId
                ORDER BY created_at DESC;
                """,
                new { request.ItemId },
                cancellationToken: cancellationToken))).ToList();

        IReadOnlyCollection<ItemAliasDto> dtos = rows
            .Select(r => new ItemAliasDto(r.Id, r.ItemId, r.Alias, r.AliasCanonical, r.Source, r.CreatedAt))
            .ToArray();
        return Result<IReadOnlyCollection<ItemAliasDto>>.Success(dtos);
    }

    private sealed record AliasRow(Guid Id, Guid ItemId, string Alias, string AliasCanonical, string Source, DateTimeOffset CreatedAt);
}

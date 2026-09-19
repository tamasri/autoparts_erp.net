using AutoPartsERP.Contracts.Inventory;
using AutoPartsERP.Domain.Wms;

namespace AutoPartsERP.Application.Features.Inventory.PickItems;

/// <summary>
/// Backs the item-picker dialog. Two modes share one search:
/// <c>sales</c> (default) lists SKUs and reads stock from <c>inventory_stock</c>, the table that drives invoicing;
/// <c>warehouse</c> lists items (so goods with no stock yet can be received) and reads <c>inventory_balances</c>.
/// Only the requested page is loaded and only that page's stock/batches are aggregated, so it stays fast on a large catalogue.
/// </summary>
public sealed record PickItemsQuery(
    string? Search,
    string Mode,
    Guid? LocationId,
    bool InStockOnly,
    int PageNumber,
    int PageSize)
    : IRequest<Result<PagedResponse<PickItemDto>>>, IAuthorizedRequest
{
    public bool IsSales => !string.Equals(Mode, "warehouse", StringComparison.OrdinalIgnoreCase);

    public string RequiredPermission => IsSales ? PermissionCodes.Invoices.Create : PermissionCodes.Inventory.Read;
}

public sealed class PickItemsQueryValidator : AbstractValidator<PickItemsQuery>
{
    public PickItemsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public sealed class PickItemsQueryHandler : IRequestHandler<PickItemsQuery, Result<PagedResponse<PickItemDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPartNumberService _partNumbers;

    public PickItemsQueryHandler(IDbConnectionFactory connectionFactory, IPartNumberService partNumbers)
    {
        _connectionFactory = connectionFactory;
        _partNumbers = partNumbers;
    }

    public async Task<Result<PagedResponse<PickItemDto>>> Handle(PickItemsQuery request, CancellationToken cancellationToken)
    {
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var canonical = search is null ? string.Empty : _partNumbers.NormalizePartNumber(search).Canonical;

        // Common predicate over the joined skus s / items i (either side may be null in warehouse mode).
        const string searchPredicate = """
            (@Search::text IS NULL
             OR s.code ILIKE @Like OR s.name ILIKE @Like OR s.name_ar ILIKE @Like OR s.barcode = @Search
             OR i.part_number ILIKE @Like OR i.name_en ILIKE @Like OR i.name_ar ILIKE @Like
             OR i.name_ar_colloquial ILIKE @Like OR i.brand ILIKE @Like
             OR (@Canonical <> '' AND i.part_number_canonical LIKE @Canonical || '%')
             OR EXISTS (SELECT 1 FROM item_aliases ia
                        WHERE ia.item_id = i.id
                          AND (ia.alias ILIKE @Like OR (@Canonical <> '' AND ia.alias_canonical LIKE @Canonical || '%'))))
            """;

        var parameters = new
        {
            Search = search,
            Like = search is null ? null : $"%{search}%",
            Canonical = canonical,
            request.LocationId,
            Offset = (request.PageNumber - 1) * request.PageSize,
            request.PageSize
        };

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var sql = request.IsSales
            ? $"""
               WITH matched AS (
                   SELECT s.id AS sku_id, i.id AS item_id,
                          COALESCE((SELECT SUM(st.quantity_available) FROM inventory_stock st
                                    WHERE st.sku_id = s.id AND (@LocationId::uuid IS NULL OR st.location_id = @LocationId)), 0) AS available
                   FROM skus s
                   LEFT JOIN items i ON i.sku_id = s.id
                   WHERE s.is_active = TRUE AND {searchPredicate}
               ),
               filtered AS (
                   SELECT *, COUNT(*) OVER() AS total_count FROM matched
                   WHERE {(request.InStockOnly ? "available > 0" : "TRUE")}
               )
               SELECT f.item_id AS ItemId, s.id AS SkuId, s.code AS Code, s.name AS Name, s.name_ar AS NameAr,
                      i.brand AS Brand, s.barcode AS Barcode, s.is_active AS IsActive, COALESCE(i.is_stop_ship, FALSE) AS IsStopShip,
                      s.has_warranty AS HasWarranty, s.is_batch_tracked AS IsBatchTracked,
                      s.selling_price_syp AS SellingPriceSyp, s.selling_price_usd AS SellingPriceUsd,
                      s.min_selling_price_syp AS MinSellingPriceSyp, s.min_selling_price_usd AS MinSellingPriceUsd,
                      f.available AS TotalAvailable, f.total_count AS TotalCount
               FROM filtered f
               INNER JOIN skus s ON s.id = f.sku_id
               LEFT JOIN items i ON i.id = f.item_id
               ORDER BY (f.available > 0) DESC, s.code
               OFFSET @Offset LIMIT @PageSize;
               """
            : $"""
               WITH matched AS (
                   SELECT i.id AS item_id, s.id AS sku_id,
                          COALESCE((SELECT SUM(ib.qty) FROM inventory_balances ib
                                    WHERE ib.item_id = i.id AND ib.status = 'AVAILABLE'
                                      AND (@LocationId::uuid IS NULL OR ib.location_id = @LocationId)), 0) AS available
                   FROM items i
                   LEFT JOIN skus s ON s.id = i.sku_id
                   WHERE i.is_active = TRUE AND {searchPredicate}
               ),
               filtered AS (
                   SELECT *, COUNT(*) OVER() AS total_count FROM matched
                   WHERE {(request.InStockOnly ? "available > 0" : "TRUE")}
               )
               SELECT i.id AS ItemId, f.sku_id AS SkuId, i.part_number AS Code, i.name_en AS Name, i.name_ar AS NameAr,
                      i.brand AS Brand, s.barcode AS Barcode, i.is_active AS IsActive, i.is_stop_ship AS IsStopShip,
                      i.has_warranty AS HasWarranty, i.is_batch_tracked AS IsBatchTracked,
                      COALESCE(s.selling_price_syp, 0) AS SellingPriceSyp, COALESCE(s.selling_price_usd, 0) AS SellingPriceUsd,
                      COALESCE(s.min_selling_price_syp, 0) AS MinSellingPriceSyp, COALESCE(s.min_selling_price_usd, 0) AS MinSellingPriceUsd,
                      f.available AS TotalAvailable, f.total_count AS TotalCount
               FROM filtered f
               INNER JOIN items i ON i.id = f.item_id
               LEFT JOIN skus s ON s.id = f.sku_id
               ORDER BY (f.available > 0) DESC, i.part_number
               OFFSET @Offset LIMIT @PageSize;
               """;

        var rows = (await connection.QueryAsync<PickRow>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))).ToList();
        var total = rows.Count == 0 ? 0 : rows[0].TotalCount;
        if (rows.Count == 0)
        {
            return Result<PagedResponse<PickItemDto>>.Success(
                new PagedResponse<PickItemDto>(Array.Empty<PickItemDto>(), request.PageNumber, request.PageSize, 0));
        }

        var stock = new Dictionary<Guid, List<PickStockDto>>();
        var keyIds = rows.Select(r => request.IsSales ? r.SkuId : r.ItemId).Where(x => x.HasValue).Select(x => x!.Value).ToArray();

        var stockSql = request.IsSales
            ? """
              SELECT st.sku_id AS Key, l.id AS LocationId, l.code AS LocationCode, l.name AS LocationName,
                     st.quantity_available AS Available, st.quantity_reserved AS Reserved
              FROM inventory_stock st
              INNER JOIN locations l ON l.id = st.location_id
              WHERE st.sku_id = ANY(@Keys) AND l.is_active = TRUE
              ORDER BY l.code;
              """
            : """
              SELECT ib.item_id AS Key, l.id AS LocationId, l.code AS LocationCode, l.name AS LocationName,
                     COALESCE(SUM(ib.qty) FILTER (WHERE ib.status = 'AVAILABLE'), 0) AS Available,
                     COALESCE(SUM(ib.qty) FILTER (WHERE ib.status = 'RESERVED'), 0) AS Reserved
              FROM inventory_balances ib
              INNER JOIN locations l ON l.id = ib.location_id
              WHERE ib.item_id = ANY(@Keys) AND l.is_active = TRUE
              GROUP BY ib.item_id, l.id, l.code, l.name
              ORDER BY l.code;
              """;

        foreach (var s in await connection.QueryAsync<StockRow>(new CommandDefinition(stockSql, new { Keys = keyIds }, cancellationToken: cancellationToken)))
        {
            if (!stock.TryGetValue(s.Key, out var list))
            {
                stock[s.Key] = list = new List<PickStockDto>();
            }

            list.Add(new PickStockDto(s.LocationId, s.LocationCode, s.LocationName, s.Available, s.Reserved));
        }

        // Batches only matter for batch-tracked SKUs that can be sold; oldest expiry first (FEFO).
        var batches = new Dictionary<Guid, List<PickBatchDto>>();
        var trackedSkuIds = rows.Where(r => r.IsBatchTracked && r.SkuId.HasValue).Select(r => r.SkuId!.Value).ToArray();
        if (trackedSkuIds.Length > 0)
        {
            var batchRows = await connection.QueryAsync<BatchRow>(new CommandDefinition(
                """
                SELECT b.sku_id AS SkuId, b.id AS Id, b.batch_number AS BatchNumber, b.location_id AS LocationId,
                       b.quantity_current AS Quantity, b.expiry_date AS ExpiryDate
                FROM batches b
                WHERE b.sku_id = ANY(@SkuIds) AND b.status = 'ACTIVE' AND b.quantity_current > 0
                ORDER BY b.expiry_date NULLS LAST, b.received_date;
                """,
                new { SkuIds = trackedSkuIds },
                cancellationToken: cancellationToken));

            foreach (var b in batchRows)
            {
                if (!batches.TryGetValue(b.SkuId, out var list))
                {
                    batches[b.SkuId] = list = new List<PickBatchDto>();
                }

                list.Add(new PickBatchDto(b.Id, b.BatchNumber, b.LocationId, b.Quantity, b.ExpiryDate));
            }
        }

        var items = rows.Select(r =>
        {
            var key = request.IsSales ? r.SkuId : r.ItemId;
            return new PickItemDto(
                r.ItemId, r.SkuId, r.Code, r.Name, r.NameAr, r.Brand, r.Barcode, r.IsActive, r.IsStopShip, r.HasWarranty,
                r.IsBatchTracked, r.SellingPriceSyp, r.SellingPriceUsd, r.MinSellingPriceSyp, r.MinSellingPriceUsd, r.TotalAvailable,
                key.HasValue && stock.TryGetValue(key.Value, out var st) ? st : new List<PickStockDto>(),
                r.SkuId.HasValue && batches.TryGetValue(r.SkuId.Value, out var bt) ? bt : new List<PickBatchDto>());
        }).ToArray();

        return Result<PagedResponse<PickItemDto>>.Success(new PagedResponse<PickItemDto>(items, request.PageNumber, request.PageSize, total));
    }

    private sealed record PickRow(
        Guid? ItemId, Guid? SkuId, string Code, string Name, string NameAr, string? Brand, string? Barcode, bool IsActive,
        bool IsStopShip, bool HasWarranty, bool IsBatchTracked, decimal SellingPriceSyp, decimal SellingPriceUsd,
        decimal MinSellingPriceSyp, decimal MinSellingPriceUsd, decimal TotalAvailable, long TotalCount);

    private sealed record StockRow(Guid Key, Guid LocationId, string LocationCode, string LocationName, decimal Available, decimal Reserved);

    private sealed record BatchRow(Guid SkuId, Guid Id, string BatchNumber, Guid LocationId, decimal Quantity, DateOnly? ExpiryDate);
}

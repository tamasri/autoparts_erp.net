using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Domain.Wms;

namespace AutoPartsERP.Infrastructure.Services;

public sealed class ItemSearchService : IItemSearchService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private readonly IDistributedCache _cache;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPartNumberService _partNumberService;
    private readonly ILogger<ItemSearchService> _logger;

    public ItemSearchService(
        IDistributedCache cache,
        IDbConnectionFactory connectionFactory,
        IPartNumberService partNumberService,
        ILogger<ItemSearchService> logger)
    {
        _cache = cache;
        _connectionFactory = connectionFactory;
        _partNumberService = partNumberService;
        _logger = logger;
    }

    public async Task<Result<PagedResponse<ItemSearchResultDto>>> SearchAsync(
        SearchItemsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Result<PagedResponse<ItemSearchResultDto>>.Success(
                new PagedResponse<ItemSearchResultDto>(Array.Empty<ItemSearchResultDto>(), 1, request.PageSize <= 0 ? 20 : request.PageSize, 0));
        }

        var normalized = _partNumberService.NormalizePartNumber(request.Query);
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = request.PageSize <= 0 ? 20 : Math.Min(request.PageSize, 100);
        var cacheKey = $"items:search:{request.Query.Trim().ToUpperInvariant()}:{pageNumber}:{pageSize}:{request.IncludeInactive}";

        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            var cachedPayload = JsonSerializer.Deserialize<PagedResponse<ItemSearchResultDto>>(cached, JsonOptions);
            if (cachedPayload is not null)
            {
                return Result<PagedResponse<ItemSearchResultDto>>.Success(cachedPayload);
            }
        }

        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);

        const string sql = """
            WITH filtered_items AS (
                SELECT
                    i.id,
                    i.part_number,
                    i.part_number_canonical,
                    i.part_number_numeric,
                    i.name_en,
                    i.name_ar,
                    i.name_ar_colloquial,
                    i.brand,
                    i.is_stop_ship,
                    CASE
                        WHEN COALESCE(SUM(CASE WHEN ib.status = 'AVAILABLE' THEN ib.qty ELSE 0 END), 0) > 0 THEN 1
                        WHEN COALESCE(SUM(CASE WHEN ib.status = 'AVAILABLE' THEN ib.qty ELSE 0 END), 0) = 0 THEN 2
                        ELSE 3
                    END AS sort_bucket,
                    COUNT(*) OVER() AS total_count
                FROM items i
                LEFT JOIN inventory_balances ib ON ib.item_id = i.id
                WHERE
                    (@IncludeInactive = TRUE OR i.is_active = TRUE)
                    AND (
                        i.part_number_canonical LIKE @CanonicalPrefix
                        OR i.part_number_numeric LIKE @NumericPrefix
                        OR i.name_ar ILIKE @LikeQuery
                        OR i.name_ar_colloquial ILIKE @LikeQuery
                        OR EXISTS (
                            SELECT 1
                            FROM item_aliases ia
                            WHERE ia.item_id = i.id
                              AND (
                                  ia.alias_canonical LIKE @CanonicalPrefix
                                  OR ia.alias ILIKE @LikeQuery
                              ))
                    )
                GROUP BY
                    i.id,
                    i.part_number,
                    i.part_number_canonical,
                    i.part_number_numeric,
                    i.name_en,
                    i.name_ar,
                    i.name_ar_colloquial,
                    i.brand,
                    i.is_stop_ship
            )
            SELECT
                id,
                part_number,
                part_number_canonical,
                part_number_numeric,
                name_en,
                name_ar,
                name_ar_colloquial,
                brand,
                is_stop_ship,
                sort_bucket,
                total_count
            FROM filtered_items
            ORDER BY sort_bucket ASC, is_stop_ship ASC, part_number_canonical ASC
            OFFSET @Offset
            LIMIT @PageSize;
            """;

        var rows = (await connection.QueryAsync<SearchRow>(
            new CommandDefinition(
                sql,
                new
                {
                    IncludeInactive = request.IncludeInactive,
                    CanonicalPrefix = $"{normalized.Canonical}%",
                    NumericPrefix = $"{normalized.Numeric}%",
                    LikeQuery = $"%{request.Query.Trim()}%",
                    Offset = (pageNumber - 1) * pageSize,
                    PageSize = pageSize
                },
                cancellationToken: cancellationToken))).ToList();

        if (rows.Count == 0)
        {
            var empty = new PagedResponse<ItemSearchResultDto>(Array.Empty<ItemSearchResultDto>(), pageNumber, pageSize, 0);
            await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(empty, JsonOptions), new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl
            }, cancellationToken);
            return Result<PagedResponse<ItemSearchResultDto>>.Success(empty);
        }

        var itemIds = rows.Select(x => x.Id).ToArray();
        var stockRows = (await connection.QueryAsync<WarehouseStockRow>(
            new CommandDefinition(
                """
                SELECT
                    ib.item_id AS ItemId,
                    l.code AS Warehouse,
                    COALESCE(SUM(CASE WHEN ib.status = 'AVAILABLE' THEN ib.qty ELSE 0 END), 0) AS AvailableQty,
                    COALESCE(SUM(CASE WHEN ib.status = 'RESERVED' THEN ib.qty ELSE 0 END), 0) AS ReservedQty,
                    EXTRACT(DAY FROM (now() - COALESCE(MAX(im.created_at), now())))::int AS LastMovementDaysAgo
                FROM inventory_balances ib
                INNER JOIN locations l ON l.id = ib.location_id
                LEFT JOIN inventory_movements im ON im.item_id = ib.item_id AND im.location_id = ib.location_id
                WHERE ib.item_id = ANY(@ItemIds)
                GROUP BY ib.item_id, l.code;
                """,
                new { ItemIds = itemIds },
                cancellationToken: cancellationToken))).ToList();

        var groupedStock = stockRows.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => (IReadOnlyCollection<ItemWarehouseStockDto>)g
            .Select(s => new ItemWarehouseStockDto(
                s.Warehouse,
                s.AvailableQty,
                s.ReservedQty,
                s.LastMovementDaysAgo <= 0 ? null : s.LastMovementDaysAgo))
            .ToArray());

        var items = rows.Select(row =>
            new ItemSearchResultDto(
                row.Id,
                row.PartNumber,
                row.PartNumberCanonical,
                row.PartNumberNumeric,
                row.NameEn,
                row.NameAr,
                row.NameArColloquial,
                row.Brand,
                row.IsStopShip,
                row.SortBucket,
                groupedStock.TryGetValue(row.Id, out var stock) ? stock : Array.Empty<ItemWarehouseStockDto>()))
            .ToArray();

        var response = new PagedResponse<ItemSearchResultDto>(items, pageNumber, pageSize, rows[0].TotalCount);
        try
        {
            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(response, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to cache item search result. Key={CacheKey}", cacheKey);
        }

        return Result<PagedResponse<ItemSearchResultDto>>.Success(response);
    }

    private sealed record SearchRow(
        Guid Id,
        string PartNumber,
        string PartNumberCanonical,
        string PartNumberNumeric,
        string NameEn,
        string NameAr,
        string? NameArColloquial,
        string? Brand,
        bool IsStopShip,
        int SortBucket,
        long TotalCount);

    private sealed record WarehouseStockRow(
        Guid ItemId,
        string Warehouse,
        decimal AvailableQty,
        decimal ReservedQty,
        int LastMovementDaysAgo);
}


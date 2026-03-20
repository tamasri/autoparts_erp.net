using Dapper;

namespace AutoPartsERP.Application.Features.Inventory.GetInventoryStock;

public sealed record GetInventoryStockQuery(
    int PageNumber = 1,
    int PageSize = 20,
    Guid? LocationId = null,
    Guid? SkuId = null,
    string? SearchTerm = null)
    : IRequest<Result<PagedResponse<InventoryStockDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Inventory.Read;
}

public sealed class GetInventoryStockQueryValidator : AbstractValidator<GetInventoryStockQuery>
{
    public GetInventoryStockQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class GetInventoryStockQueryHandler : IRequestHandler<GetInventoryStockQuery, Result<PagedResponse<InventoryStockDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetInventoryStockQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<InventoryStockDto>>> Handle(GetInventoryStockQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (request.LocationId.HasValue)
        {
            conditions.Add("st.location_id = @LocationId");
            parameters.Add("LocationId", request.LocationId.Value);
        }

        if (request.SkuId.HasValue)
        {
            conditions.Add("st.sku_id = @SkuId");
            parameters.Add("SkuId", request.SkuId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            conditions.Add("(s.code ILIKE @Search OR s.name ILIKE @Search OR s.name_ar ILIKE @Search)");
            parameters.Add("Search", $"%{request.SearchTerm.Trim()}%");
        }

        var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
        parameters.Add("Offset", (request.PageNumber - 1) * request.PageSize);
        parameters.Add("PageSize", request.PageSize);

        var items = (await connection.QueryAsync<InventoryStockDto>(
            new CommandDefinition($"""
                SELECT
                    st.id AS Id,
                    st.sku_id AS SkuId,
                    s.code AS SkuCode,
                    s.name AS SkuName,
                    st.location_id AS LocationId,
                    l.code AS LocationCode,
                    st.quantity_on_hand AS QuantityOnHand,
                    st.quantity_reserved AS QuantityReserved,
                    st.quantity_available AS QuantityAvailable,
                    CASE WHEN st.quantity_available <= s.reorder_level THEN TRUE ELSE FALSE END AS LowStockFlag,
                    st.quantity_on_hand::text AS QuantityDisplay
                FROM inventory_stock st
                INNER JOIN skus s ON s.id = st.sku_id
                INNER JOIN locations l ON l.id = st.location_id
                {where}
                ORDER BY s.code, l.code
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """,
                parameters,
                cancellationToken: cancellationToken))).ToArray();

        var total = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition($"""
                SELECT COUNT(*)
                FROM inventory_stock st
                INNER JOIN skus s ON s.id = st.sku_id
                INNER JOIN locations l ON l.id = st.location_id
                {where};
                """,
                parameters,
                cancellationToken: cancellationToken));

        return Result<PagedResponse<InventoryStockDto>>.Success(new PagedResponse<InventoryStockDto>(items, request.PageNumber, request.PageSize, total));
    }
}

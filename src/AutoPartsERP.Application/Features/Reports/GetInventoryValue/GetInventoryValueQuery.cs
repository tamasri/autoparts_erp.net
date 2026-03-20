using Dapper;

namespace AutoPartsERP.Application.Features.Reports.GetInventoryValue;

public sealed record GetInventoryValueQuery(Guid? LocationId = null, Guid? CategoryId = null)
    : IRequest<Result<IReadOnlyCollection<InventoryValueRowDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Reports.InventoryValue;
}

public sealed class GetInventoryValueQueryValidator : AbstractValidator<GetInventoryValueQuery>
{
    public GetInventoryValueQueryValidator()
    {
    }
}

public sealed class GetInventoryValueQueryHandler : IRequestHandler<GetInventoryValueQuery, Result<IReadOnlyCollection<InventoryValueRowDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetInventoryValueQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<IReadOnlyCollection<InventoryValueRowDto>>> Handle(GetInventoryValueQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (request.LocationId.HasValue)
        {
            conditions.Add("st.location_id = @LocationId");
            parameters.Add("LocationId", request.LocationId.Value);
        }

        if (request.CategoryId.HasValue)
        {
            conditions.Add("s.category_id = @CategoryId");
            parameters.Add("CategoryId", request.CategoryId.Value);
        }

        var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);

        var rows = (await connection.QueryAsync<InventoryValueRowDto>(
            new CommandDefinition($"""
                SELECT
                    s.id AS SkuId,
                    s.code AS SkuCode,
                    s.name AS SkuName,
                    COALESCE(SUM(st.quantity_on_hand), 0) AS QuantityOnHand,
                    COALESCE(SUM(st.quantity_reserved), 0) AS QuantityReserved,
                    COALESCE(SUM(st.quantity_available), 0) AS QuantityAvailable,
                    COALESCE(AVG(s.cost_price_syp), 0) AS UnitCostSyp,
                    COALESCE(AVG(s.cost_price_usd), 0) AS UnitCostUsd,
                    COALESCE(SUM(st.quantity_on_hand * s.cost_price_syp), 0) AS TotalValueSyp,
                    COALESCE(SUM(st.quantity_on_hand * s.cost_price_usd), 0) AS TotalValueUsd,
                    CASE WHEN COALESCE(SUM(st.quantity_available), 0) <= s.reorder_level THEN TRUE ELSE FALSE END AS LowStockFlag
                FROM skus s
                LEFT JOIN inventory_stock st ON st.sku_id = s.id
                {where}
                GROUP BY s.id, s.code, s.name, s.reorder_level;
                """,
                parameters,
                cancellationToken: cancellationToken))).ToArray();

        return Result<IReadOnlyCollection<InventoryValueRowDto>>.Success(rows);
    }
}

using Dapper;
using System.Text;

namespace AutoPartsERP.Application.Features.Reports.ExportInventoryValue;

public sealed record ExportInventoryValueCommand(Guid? LocationId = null, Guid? CategoryId = null)
    : IRequest<Result<byte[]>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Reports.InventoryValue;
}

public sealed class ExportInventoryValueCommandValidator : AbstractValidator<ExportInventoryValueCommand>
{
    public ExportInventoryValueCommandValidator()
    {
    }
}

public sealed class ExportInventoryValueCommandHandler : IRequestHandler<ExportInventoryValueCommand, Result<byte[]>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ExportInventoryValueCommandHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<byte[]>> Handle(ExportInventoryValueCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<InventoryValueRowDto>(
            new CommandDefinition(
                """
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
                GROUP BY s.id, s.code, s.name, s.reorder_level;
                """,
                cancellationToken: cancellationToken))).ToArray();

        var csv = new StringBuilder();
        csv.AppendLine("SkuCode,SkuName,QuantityOnHand,QuantityReserved,QuantityAvailable,UnitCostSyp,UnitCostUsd,TotalValueSyp,TotalValueUsd,LowStockFlag");
        foreach (var row in rows)
        {
            csv.AppendLine(string.Join(",",
                Escape(row.SkuCode),
                Escape(row.SkuName),
                row.QuantityOnHand.ToString("0.####"),
                row.QuantityReserved.ToString("0.####"),
                row.QuantityAvailable.ToString("0.####"),
                row.UnitCostSyp.ToString("0.####"),
                row.UnitCostUsd.ToString("0.####"),
                row.TotalValueSyp.ToString("0.####"),
                row.TotalValueUsd.ToString("0.####"),
                row.LowStockFlag));
        }

        return Result<byte[]>.Success(Encoding.UTF8.GetBytes(csv.ToString()));
    }

    private static string Escape(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}

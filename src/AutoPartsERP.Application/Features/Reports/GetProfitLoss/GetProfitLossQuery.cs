using Dapper;

namespace AutoPartsERP.Application.Features.Reports.GetProfitLoss;

public sealed record GetProfitLossQuery(int Year, int? Month)
    : IRequest<Result<PagedResponse<ProfitLossRowDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Reports.ProfitLoss;
}

public sealed class GetProfitLossQueryValidator : AbstractValidator<GetProfitLossQuery>
{
    public GetProfitLossQueryValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2020, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12).When(x => x.Month.HasValue);
    }
}

public sealed class GetProfitLossQueryHandler : IRequestHandler<GetProfitLossQuery, Result<PagedResponse<ProfitLossRowDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetProfitLossQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<ProfitLossRowDto>>> Handle(GetProfitLossQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var parameters = new DynamicParameters();
        parameters.Add("Year", request.Year);
        parameters.Add("Month", request.Month);

        var rows = (await connection.QueryAsync<ProfitLossRowDto>(
            new CommandDefinition(
                """
                SELECT
                    year AS Year,
                    month AS Month,
                    total_revenue_syp AS TotalRevenueSyp,
                    total_revenue_usd AS TotalRevenueUsd,
                    total_cogs_syp AS TotalCogsSyp,
                    total_cogs_usd AS TotalCogsUsd,
                    gross_profit_syp AS GrossProfitSyp,
                    gross_profit_usd AS GrossProfitUsd,
                    gross_margin_pct AS GrossMarginPct,
                    year::text AS PeriodDisplay
                FROM monthly_pl_summary
                WHERE year = @Year AND (@Month IS NULL OR month = @Month)
                ORDER BY year, month;
                """,
                parameters,
                cancellationToken: cancellationToken))).ToArray();

        var mapped = rows.Select(row => row with { PeriodDisplay = ReportMappings.PeriodDisplay(row.Year, row.Month) }).ToArray();

        return Result<PagedResponse<ProfitLossRowDto>>.Success(new PagedResponse<ProfitLossRowDto>(mapped, 1, mapped.Length == 0 ? 0 : mapped.Length, mapped.Length));
    }
}

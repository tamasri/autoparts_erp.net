namespace AutoPartsERP.Application.Features.Kpi.GetKpiDefinitions;

public sealed record GetKpiDefinitionsQuery()
    : IRequest<Result<IReadOnlyCollection<KpiDefinitionDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.System.ConfigRead;
}

public sealed class GetKpiDefinitionsQueryHandler : IRequestHandler<GetKpiDefinitionsQuery, Result<IReadOnlyCollection<KpiDefinitionDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetKpiDefinitionsQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<IReadOnlyCollection<KpiDefinitionDto>>> Handle(GetKpiDefinitionsQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<KpiDefinitionDto>(new CommandDefinition(
            """
            SELECT
                id AS Id,
                key AS Key,
                domain AS Domain,
                title AS Title,
                title_ar AS TitleAr,
                unit AS Unit,
                direction AS Direction,
                description AS Description,
                is_active AS IsActive,
                created_at AS CreatedAt
            FROM kpi_definitions
            ORDER BY created_at DESC;
            """,
            cancellationToken: cancellationToken));

        return Result<IReadOnlyCollection<KpiDefinitionDto>>.Success(rows.ToArray());
    }
}

using AutoPartsERP.Contracts.Inventory;

namespace AutoPartsERP.Application.Features.Locations;

/// <summary>Active storage locations (warehouses, shelves, vehicles, ...) for dropdowns. The set is small, so it is not paged.</summary>
public sealed record GetLocationsQuery(string? Type)
    : IRequest<Result<IReadOnlyCollection<LocationDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Inventory.Read;
}

public sealed class GetLocationsQueryHandler : IRequestHandler<GetLocationsQuery, Result<IReadOnlyCollection<LocationDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetLocationsQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<IReadOnlyCollection<LocationDto>>> Handle(GetLocationsQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<LocationDto>(new CommandDefinition(
            """
            SELECT id AS Id, code AS Code, name AS Name, type AS Type, parent_id AS ParentId
            FROM locations
            WHERE is_active = TRUE AND (@Type::text IS NULL OR type = @Type)
            ORDER BY code;
            """,
            new { Type = string.IsNullOrWhiteSpace(request.Type) ? null : request.Type.Trim().ToUpperInvariant() },
            cancellationToken: cancellationToken))).ToArray();

        return Result<IReadOnlyCollection<LocationDto>>.Success(rows);
    }
}

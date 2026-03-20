namespace AutoPartsERP.Application.Features.Catalog.GetCategoryTree;

public sealed record GetCategoryTreeQuery()
    : IRequest<Result<IReadOnlyCollection<CategoryDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Catalog.Read;
}

public sealed class GetCategoryTreeQueryValidator : AbstractValidator<GetCategoryTreeQuery>
{
}

public sealed class GetCategoryTreeQueryHandler : IRequestHandler<GetCategoryTreeQuery, Result<IReadOnlyCollection<CategoryDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetCategoryTreeQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<IReadOnlyCollection<CategoryDto>>> Handle(GetCategoryTreeQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, path::text AS path, name, name_ar, parent_id, depth, is_active
            FROM categories
            ORDER BY path;
            """;

        var items = new List<CategoryDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CategoryDto(
                reader.GetGuid(reader.GetOrdinal("id")),
                reader.GetString(reader.GetOrdinal("path")),
                reader.GetString(reader.GetOrdinal("name")),
                reader.IsDBNull(reader.GetOrdinal("name_ar")) ? null : reader.GetString(reader.GetOrdinal("name_ar")),
                reader.IsDBNull(reader.GetOrdinal("parent_id")) ? null : reader.GetGuid(reader.GetOrdinal("parent_id")),
                reader.GetInt32(reader.GetOrdinal("depth")),
                reader.GetBoolean(reader.GetOrdinal("is_active"))));
        }

        return Result<IReadOnlyCollection<CategoryDto>>.Success(items);
    }
}

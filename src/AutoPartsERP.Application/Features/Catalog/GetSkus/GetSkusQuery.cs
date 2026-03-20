namespace AutoPartsERP.Application.Features.Catalog.GetSkus;

public sealed record GetSkusQuery(SkuQueryRequest Request)
    : IRequest<Result<PagedResponse<SkuDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Catalog.Read;
}

public sealed class GetSkusQueryValidator : AbstractValidator<GetSkusQuery>
{
    public GetSkusQueryValidator()
    {
        RuleFor(x => x.Request.PageNumber).GreaterThan(0);
        RuleFor(x => x.Request.PageSize).InclusiveBetween(1, 1000);
    }
}

public sealed class GetSkusQueryHandler : IRequestHandler<GetSkusQuery, Result<PagedResponse<SkuDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetSkusQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<SkuDto>>> Handle(GetSkusQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var where = new List<string>();
        if (request.Request.CategoryId.HasValue)
        {
            where.Add("c.path <@ (SELECT path FROM categories WHERE id = @CategoryId)");
        }
        if (request.Request.IsActive.HasValue)
        {
            where.Add("s.is_active = @IsActive");
        }
        if (request.Request.IsBatchTracked.HasValue)
        {
            where.Add("s.is_batch_tracked = @IsBatchTracked");
        }
        if (request.Request.HasWarranty.HasValue)
        {
            where.Add("s.has_warranty = @HasWarranty");
        }
        if (!string.IsNullOrWhiteSpace(request.Request.SearchTerm))
        {
            where.Add("(s.code ILIKE @Search OR s.name ILIKE @Search OR s.name_ar ILIKE @Search OR s.barcode ILIKE @Search)");
        }
        if (request.Request.Tags is { Length: > 0 })
        {
            where.Add("s.tags && @Tags");
        }

        var whereClause = where.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", where)}";
        var parameters = new Dictionary<string, object?>
        {
            ["CategoryId"] = request.Request.CategoryId,
            ["IsActive"] = request.Request.IsActive,
            ["IsBatchTracked"] = request.Request.IsBatchTracked,
            ["HasWarranty"] = request.Request.HasWarranty,
            ["Search"] = string.IsNullOrWhiteSpace(request.Request.SearchTerm) ? null : $"%{request.Request.SearchTerm.Trim()}%",
            ["Tags"] = request.Request.Tags is { Length: > 0 } ? request.Request.Tags : null
        };

        var total = await CountAsync(connection, $"""
            SELECT COUNT(*)
            FROM skus s
            INNER JOIN categories c ON c.id = s.category_id
            {whereClause};
            """, parameters, cancellationToken);

        parameters["Offset"] = (request.Request.PageNumber - 1) * request.Request.PageSize;
        parameters["PageSize"] = request.Request.PageSize;

        var rows = await QueryAsync(connection, $"""
            SELECT s.id, s.code, s.name, s.name_ar, s.category_id, s.barcode, s.is_batch_tracked, s.has_warranty,
                   s.warranty_months, s.selling_price_syp, s.selling_price_usd, s.min_selling_price_syp,
                   s.min_selling_price_usd, s.is_active, s.tags
            FROM skus s
            INNER JOIN categories c ON c.id = s.category_id
            {whereClause}
            ORDER BY s.name
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, parameters, cancellationToken);

        return Result<PagedResponse<SkuDto>>.Success(new PagedResponse<SkuDto>(rows, request.Request.PageNumber, request.Request.PageSize, total));
    }

    private static async Task<long> CountAsync(DbConnection connection, string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar is null or DBNull ? 0L : Convert.ToInt64(scalar);
    }

    private static async Task<IReadOnlyCollection<SkuDto>> QueryAsync(DbConnection connection, string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);

        var items = new List<SkuDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new SkuDto(
                reader.GetGuid(reader.GetOrdinal("id")),
                reader.GetString(reader.GetOrdinal("code")),
                reader.GetString(reader.GetOrdinal("name")),
                reader.GetString(reader.GetOrdinal("name_ar")),
                reader.GetGuid(reader.GetOrdinal("category_id")),
                reader.IsDBNull(reader.GetOrdinal("barcode")) ? null : reader.GetString(reader.GetOrdinal("barcode")),
                reader.GetBoolean(reader.GetOrdinal("is_batch_tracked")),
                reader.GetBoolean(reader.GetOrdinal("has_warranty")),
                reader.GetInt32(reader.GetOrdinal("warranty_months")),
                reader.GetDecimal(reader.GetOrdinal("selling_price_syp")),
                reader.GetDecimal(reader.GetOrdinal("selling_price_usd")),
                reader.GetDecimal(reader.GetOrdinal("min_selling_price_syp")),
                reader.GetDecimal(reader.GetOrdinal("min_selling_price_usd")),
                reader.GetBoolean(reader.GetOrdinal("is_active")),
                reader.IsDBNull(reader.GetOrdinal("tags")) ? Array.Empty<string>() : (string[])reader["tags"]));
        }

        return items;
    }

    private static void AddParameters(DbCommand command, IReadOnlyDictionary<string, object?> parameters)
    {
        foreach (var parameter in parameters)
        {
            var dbParameter = command.CreateParameter();
            dbParameter.ParameterName = parameter.Key;
            dbParameter.Value = parameter.Value ?? DBNull.Value;
            command.Parameters.Add(dbParameter);
        }
    }
}

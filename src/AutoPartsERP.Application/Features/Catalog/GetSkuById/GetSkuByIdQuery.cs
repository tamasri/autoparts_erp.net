namespace AutoPartsERP.Application.Features.Catalog.GetSkuById;

public sealed record GetSkuByIdQuery(Guid SkuId)
    : IRequest<Result<SkuDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Catalog.Read;
}

public sealed class GetSkuByIdQueryValidator : AbstractValidator<GetSkuByIdQuery>
{
    public GetSkuByIdQueryValidator()
    {
        RuleFor(x => x.SkuId).NotEmpty();
    }
}

public sealed class GetSkuByIdQueryHandler : IRequestHandler<GetSkuByIdQuery, Result<SkuDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetSkuByIdQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<SkuDto>> Handle(GetSkuByIdQuery request, CancellationToken cancellationToken)
    {
        return await QuerySingleAsync(request.SkuId, "WHERE s.id = @Id", cancellationToken);
    }

    private async Task<Result<SkuDto>> QuerySingleAsync(Guid skuId, string filterSql, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT s.id, s.code, s.name, s.name_ar, s.category_id, s.barcode, s.is_batch_tracked, s.has_warranty,
                   s.warranty_months, s.selling_price_syp, s.selling_price_usd, s.min_selling_price_syp,
                   s.min_selling_price_usd, s.is_active, s.tags
            FROM skus s
            INNER JOIN categories c ON c.id = s.category_id
            {filterSql};
            """;
        AddParameter(command, "Id", skuId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result<SkuDto>.Failure(new Error("Sku.NotFound", "SKU was not found."));
        }

        return Result<SkuDto>.Success(MapSku(reader));
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static SkuDto MapSku(DbDataReader reader)
    {
        return new SkuDto(
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
            reader.IsDBNull(reader.GetOrdinal("tags")) ? Array.Empty<string>() : (string[])reader["tags"]);
    }
}

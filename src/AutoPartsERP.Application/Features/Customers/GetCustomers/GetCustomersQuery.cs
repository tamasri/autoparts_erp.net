namespace AutoPartsERP.Application.Features.Customers.GetCustomers;

public sealed record GetCustomersQuery(CustomerQueryRequest Request)
    : IRequest<Result<PagedResponse<CustomerDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Customers.Read;
}

public sealed class GetCustomersQueryValidator : AbstractValidator<GetCustomersQuery>
{
    public GetCustomersQueryValidator()
    {
        RuleFor(x => x.Request.PageNumber).GreaterThan(0);
        RuleFor(x => x.Request.PageSize).InclusiveBetween(1, 1000);
    }
}

public sealed class GetCustomersQueryHandler : IRequestHandler<GetCustomersQuery, Result<PagedResponse<CustomerDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetCustomersQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<CustomerDto>>> Handle(GetCustomersQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        await EnsureOpenAsync(connection, cancellationToken);

        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Request.Type))
        {
            where.Add("type = @Type");
        }

        if (request.Request.IsActive.HasValue)
        {
            where.Add("is_active = @IsActive");
        }

        if (!string.IsNullOrWhiteSpace(request.Request.SearchTerm))
        {
            where.Add("(code ILIKE @Search OR name ILIKE @Search OR phone ILIKE @Search)");
        }

        if (request.Request.AssignedSalesRep.HasValue)
        {
            where.Add("assigned_sales_rep = @AssignedSalesRep");
        }

        var whereClause = where.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", where)}";

        var parameters = new Dictionary<string, object?>
        {
            ["Type"] = request.Request.Type?.Trim().ToUpperInvariant(),
            ["IsActive"] = request.Request.IsActive,
            ["Search"] = string.IsNullOrWhiteSpace(request.Request.SearchTerm) ? null : $"%{request.Request.SearchTerm.Trim()}%",
            ["AssignedSalesRep"] = request.Request.AssignedSalesRep
        };

        var total = await ExecuteScalarAsync(connection, $"""
            SELECT COUNT(*) FROM customers {whereClause};
            """, parameters, cancellationToken);

        var offset = (request.Request.PageNumber - 1) * request.Request.PageSize;
        parameters["Offset"] = offset;
        parameters["PageSize"] = request.Request.PageSize;
        var rows = await QueryCustomersAsync(connection, $"""
            SELECT id, code, name, type, phone, address, credit_limit_syp, credit_limit_usd,
                   payment_terms_days, is_active, assigned_sales_rep, created_at, updated_at
            FROM customers
            {whereClause}
            ORDER BY name
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """,
            parameters,
            cancellationToken);

        return Result<PagedResponse<CustomerDto>>.Success(new PagedResponse<CustomerDto>(rows, request.Request.PageNumber, request.Request.PageSize, total));
    }

    private static async Task EnsureOpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }

    private static async Task<long> ExecuteScalarAsync(DbConnection connection, string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            var dbParameter = command.CreateParameter();
            dbParameter.ParameterName = parameter.Key;
            dbParameter.Value = parameter.Value ?? DBNull.Value;
            command.Parameters.Add(dbParameter);
        }

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar is null or DBNull ? 0L : Convert.ToInt64(scalar);
    }

    private static async Task<IReadOnlyCollection<CustomerDto>> QueryCustomersAsync(DbConnection connection, string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            var dbParameter = command.CreateParameter();
            dbParameter.ParameterName = parameter.Key;
            dbParameter.Value = parameter.Value ?? DBNull.Value;
            command.Parameters.Add(dbParameter);
        }

        var items = new List<CustomerDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(MapCustomer(reader));
        }

        return items;
    }

    private static CustomerDto MapCustomer(DbDataReader reader)
    {
        return new CustomerDto(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("code")),
            reader.GetString(reader.GetOrdinal("name")),
            reader.GetString(reader.GetOrdinal("type")),
            reader.IsDBNull(reader.GetOrdinal("phone")) ? null : reader.GetString(reader.GetOrdinal("phone")),
            reader.IsDBNull(reader.GetOrdinal("address")) ? null : reader.GetString(reader.GetOrdinal("address")),
            reader.GetDecimal(reader.GetOrdinal("credit_limit_syp")),
            reader.GetDecimal(reader.GetOrdinal("credit_limit_usd")),
            reader.GetInt32(reader.GetOrdinal("payment_terms_days")),
            reader.GetBoolean(reader.GetOrdinal("is_active")),
            reader.IsDBNull(reader.GetOrdinal("assigned_sales_rep")) ? null : reader.GetGuid(reader.GetOrdinal("assigned_sales_rep")),
            reader.GetBoolean(reader.GetOrdinal("is_active")) ? "نشط" : "غير نشط",
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            reader.IsDBNull(reader.GetOrdinal("updated_at")) ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")));
    }
}

namespace AutoPartsERP.Application.Features.Customers.GetCustomerById;

public sealed record GetCustomerByIdQuery(Guid CustomerId)
    : IRequest<Result<CustomerDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Customers.Read;
}

public sealed class GetCustomerByIdQueryValidator : AbstractValidator<GetCustomerByIdQuery>
{
    public GetCustomerByIdQueryValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
    }
}

public sealed class GetCustomerByIdQueryHandler : IRequestHandler<GetCustomerByIdQuery, Result<CustomerDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetCustomerByIdQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<CustomerDto>> Handle(GetCustomerByIdQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, name, type, phone, address, credit_limit_syp, credit_limit_usd,
                   payment_terms_days, is_active, assigned_sales_rep, created_at, updated_at
            FROM customers
            WHERE id = @Id;
            """;

        AddParameter(command, "Id", request.CustomerId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result<CustomerDto>.Failure(new Error("Customer.NotFound", "Customer was not found."));
        }

        return Result<CustomerDto>.Success(MapCustomer(reader));
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
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

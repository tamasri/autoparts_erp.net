namespace AutoPartsERP.Application.Features.Customers.DeactivateCustomer;

public sealed record DeactivateCustomerCommand(Guid CustomerId, string Reason)
    : IRequest<Result<CustomerDto>>, IAuthorizedRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.Customers.Deactivate;
    public string AuditModule => "CUSTOMERS";
    public bool RequiresApproval => true;
}

public sealed class DeactivateCustomerCommandValidator : AbstractValidator<DeactivateCustomerCommand>
{
    public DeactivateCustomerCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3).MaximumLength(500);
    }
}

public sealed class DeactivateCustomerCommandHandler : IRequestHandler<DeactivateCustomerCommand, Result<CustomerDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public DeactivateCustomerCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<CustomerDto>> Handle(DeactivateCustomerCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        await EnsureOpenAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE customers
            SET is_active = FALSE,
                notes = COALESCE(notes, '') || CASE WHEN COALESCE(notes, '') = '' THEN '' ELSE E'\n' END || @Reason,
                updated_at = now(),
                updated_by = @UpdatedBy
            WHERE id = @Id
            RETURNING id, code, name, type, phone, address, credit_limit_syp, credit_limit_usd,
                      payment_terms_days, is_active, assigned_sales_rep, created_at, updated_at;
            """;

        AddParameter(command, "Id", request.CustomerId);
        AddParameter(command, "Reason", request.Reason.Trim());
        AddParameter(command, "UpdatedBy", _currentUser.UserId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result<CustomerDto>.Failure(new Error("Customer.NotFound", "Customer was not found."));
        }

        return Result<CustomerDto>.Success(MapCustomer(reader));
    }

    private static async Task EnsureOpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
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

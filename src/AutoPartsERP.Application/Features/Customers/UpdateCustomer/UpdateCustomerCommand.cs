namespace AutoPartsERP.Application.Features.Customers.UpdateCustomer;

public sealed record UpdateCustomerCommand(Guid CustomerId, UpdateCustomerRequest Request)
    : IRequest<Result<CustomerDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Customers.Update;
    public string AuditModule => "CUSTOMERS";
}

public sealed class UpdateCustomerCommandValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Request.Name).NotEmpty().Length(2, 200);
        RuleFor(x => x.Request.CreditLimitSyp).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.CreditLimitUsd).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.PaymentTermsDays).InclusiveBetween(0, 365);
        RuleFor(x => x.Request.Type).NotEmpty();
    }
}

public sealed class UpdateCustomerCommandHandler : IRequestHandler<UpdateCustomerCommand, Result<CustomerDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public UpdateCustomerCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<CustomerDto>> Handle(UpdateCustomerCommand request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<CustomerType>(request.Request.Type, true, out var customerType))
        {
            return Result<CustomerDto>.Failure(new Error("Customer.TypeInvalid", "Customer type is invalid."));
        }

        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        await EnsureOpenAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE customers
            SET name = @Name,
                type = @Type,
                phone = @Phone,
                phone2 = @Phone2,
                address = @Address,
                city = @City,
                credit_limit_syp = @CreditLimitSyp,
                credit_limit_usd = @CreditLimitUsd,
                payment_terms_days = @PaymentTermsDays,
                assigned_sales_rep = @AssignedSalesRep,
                notes = @Notes,
                updated_at = now(),
                updated_by = @UpdatedBy
            WHERE id = @Id
            RETURNING id, code, name, type, phone, address, credit_limit_syp, credit_limit_usd,
                      payment_terms_days, is_active, assigned_sales_rep, created_at, updated_at;
            """;

        AddParameter(command, "Id", request.CustomerId);
        AddParameter(command, "Name", request.Request.Name.Trim());
        AddParameter(command, "Type", customerType.ToString().ToUpperInvariant());
        AddParameter(command, "Phone", (object?)request.Request.Phone?.Trim() ?? DBNull.Value);
        AddParameter(command, "Phone2", (object?)request.Request.Phone2?.Trim() ?? DBNull.Value);
        AddParameter(command, "Address", (object?)request.Request.Address?.Trim() ?? DBNull.Value);
        AddParameter(command, "City", (object?)request.Request.City?.Trim() ?? DBNull.Value);
        AddParameter(command, "CreditLimitSyp", request.Request.CreditLimitSyp);
        AddParameter(command, "CreditLimitUsd", request.Request.CreditLimitUsd);
        AddParameter(command, "PaymentTermsDays", request.Request.PaymentTermsDays);
        AddParameter(command, "AssignedSalesRep", (object?)request.Request.AssignedSalesRep ?? DBNull.Value);
        AddParameter(command, "Notes", (object?)request.Request.Notes?.Trim() ?? DBNull.Value);
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

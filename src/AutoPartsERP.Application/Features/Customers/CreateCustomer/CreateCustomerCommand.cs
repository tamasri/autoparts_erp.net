namespace AutoPartsERP.Application.Features.Customers.CreateCustomer;

public sealed record CreateCustomerCommand(CreateCustomerRequest Request, string IdempotencyKey)
    : IRequest<Result<CustomerDto>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Customers.Create;
    public string AuditModule => "CUSTOMERS";
}

public sealed class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    private static readonly Regex CodePattern = new("^[A-Z0-9\\-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public CreateCustomerCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50).Must(code => CodePattern.IsMatch(code));
        RuleFor(x => x.Request.Name).NotEmpty().Length(2, 200);
        RuleFor(x => x.Request.CreditLimitSyp).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.CreditLimitUsd).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.PaymentTermsDays).InclusiveBetween(0, 365);
        RuleFor(x => x.Request.Type).NotEmpty();
    }
}

public sealed class CreateCustomerCommandHandler : IRequestHandler<CreateCustomerCommand, Result<CustomerDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreateCustomerCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<CustomerDto>> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<CustomerType>(request.Request.Type, true, out var customerType))
        {
            return Result<CustomerDto>.Failure(new Error("Customer.TypeInvalid", "Customer type is invalid."));
        }

        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        await EnsureOpenAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var partyId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var partyCode = await GetNextPartyCodeAsync(connection, transaction, cancellationToken);

        await using (var partyCommand = connection.CreateCommand())
        {
            partyCommand.Transaction = transaction;
            partyCommand.CommandText = """
                INSERT INTO parties
                    (id, code, display_name, display_name_ar, tax_number, website, notes, is_active, created_at, created_by)
                VALUES
                    (@Id, @Code, @DisplayName, @DisplayNameAr, NULL, NULL, @Notes, TRUE, now(), @CreatedBy);
                """;

            AddParameter(partyCommand, "Id", partyId);
            AddParameter(partyCommand, "Code", partyCode);
            AddParameter(partyCommand, "DisplayName", request.Request.Name.Trim());
            AddParameter(partyCommand, "DisplayNameAr", request.Request.Name.Trim());
            AddParameter(partyCommand, "Notes", (object?)request.Request.Notes?.Trim() ?? DBNull.Value);
            AddParameter(partyCommand, "CreatedBy", _currentUser.UserId);
            await partyCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var typeAssignmentCommand = connection.CreateCommand())
        {
            typeAssignmentCommand.Transaction = transaction;
            typeAssignmentCommand.CommandText = """
                INSERT INTO party_type_assignments
                    (id, party_id, type_code, is_active, requested_by, approved_by, activated_at, created_at)
                VALUES
                    (@Id, @PartyId, 'CUSTOMER', TRUE, @RequestedBy, @RequestedBy, now(), now());
                """;

            AddParameter(typeAssignmentCommand, "Id", Guid.NewGuid());
            AddParameter(typeAssignmentCommand, "PartyId", partyId);
            AddParameter(typeAssignmentCommand, "RequestedBy", _currentUser.UserId);
            await typeAssignmentCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO customers
                (id, party_id, code, name, type, phone, phone2, address, city, region, tax_number,
                 credit_limit_syp, credit_limit_usd, payment_terms_days, is_active,
                 assigned_sales_rep, notes, created_by, updated_by)
            VALUES
                (@Id, @PartyId, @Code, @Name, @Type, @Phone, @Phone2, @Address, @City, @Region, @TaxNumber,
                 @CreditLimitSyp, @CreditLimitUsd, @PaymentTermsDays, TRUE,
                 @AssignedSalesRep, @Notes, @CreatedBy, NULL)
            RETURNING id, code, name, type, phone, address, credit_limit_syp, credit_limit_usd,
                      payment_terms_days, is_active, assigned_sales_rep, created_at, updated_at;
            """;

        AddParameter(command, "Id", customerId);
        AddParameter(command, "PartyId", partyId);
        AddParameter(command, "Code", request.Request.Code.Trim().ToUpperInvariant());
        AddParameter(command, "Name", request.Request.Name.Trim());
        AddParameter(command, "Type", customerType.ToString().ToUpperInvariant());
        AddParameter(command, "Phone", (object?)request.Request.Phone?.Trim() ?? DBNull.Value);
        AddParameter(command, "Phone2", (object?)request.Request.Phone2?.Trim() ?? DBNull.Value);
        AddParameter(command, "Address", (object?)request.Request.Address?.Trim() ?? DBNull.Value);
        AddParameter(command, "City", (object?)request.Request.City?.Trim() ?? DBNull.Value);
        AddParameter(command, "Region", DBNull.Value);
        AddParameter(command, "TaxNumber", DBNull.Value);
        AddParameter(command, "CreditLimitSyp", request.Request.CreditLimitSyp);
        AddParameter(command, "CreditLimitUsd", request.Request.CreditLimitUsd);
        AddParameter(command, "PaymentTermsDays", request.Request.PaymentTermsDays);
        AddParameter(command, "AssignedSalesRep", (object?)request.Request.AssignedSalesRep ?? DBNull.Value);
        AddParameter(command, "Notes", (object?)request.Request.Notes?.Trim() ?? DBNull.Value);
        AddParameter(command, "CreatedBy", _currentUser.UserId);

        CustomerDto created;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<CustomerDto>.Failure(new Error("Customer.CreateFailed", "Customer could not be created."));
            }

            created = MapCustomer(reader);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<CustomerDto>.Success(created);
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

    private static async Task<string> GetNextPartyCodeAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 'PTY-' || LPAD(nextval('party_code_seq')::text, 4, '0');";
        var raw = await command.ExecuteScalarAsync(cancellationToken);
        return raw?.ToString() ?? $"PTY-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
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

namespace AutoPartsERP.Application.Features.Customers.GetCustomerAccountStatement;

public sealed record GetCustomerAccountStatementQuery(Guid CustomerId)
    : IRequest<Result<CustomerAccountStatementDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Reports.AccountStatement;
}

public sealed class GetCustomerAccountStatementQueryValidator : AbstractValidator<GetCustomerAccountStatementQuery>
{
    public GetCustomerAccountStatementQueryValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
    }
}

public sealed class GetCustomerAccountStatementQueryHandler : IRequestHandler<GetCustomerAccountStatementQuery, Result<CustomerAccountStatementDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetCustomerAccountStatementQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<CustomerAccountStatementDto>> Handle(GetCustomerAccountStatementQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var customer = await GetCustomerAsync(connection, request.CustomerId, cancellationToken);
        if (customer is null)
        {
            return Result<CustomerAccountStatementDto>.Failure(new Error("Customer.NotFound", "Customer was not found."));
        }

        var summary = await GetSummaryAsync(connection, request.CustomerId, cancellationToken);
        var transactions = await GetTransactionsAsync(connection, request.CustomerId, cancellationToken);

        return Result<CustomerAccountStatementDto>.Success(new CustomerAccountStatementDto(
            customer.Id,
            customer.Code,
            customer.Name,
            summary.TotalInvoicedSyp,
            summary.TotalInvoicedUsd,
            summary.TotalPaidSyp,
            summary.TotalPaidUsd,
            summary.OutstandingSyp,
            summary.OutstandingUsd,
            transactions));
    }

    private static async Task<CustomerDto?> GetCustomerAsync(DbConnection connection, Guid customerId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, name, type, phone, address, credit_limit_syp, credit_limit_usd,
                   payment_terms_days, is_active, assigned_sales_rep, created_at, updated_at
            FROM customers
            WHERE id = @Id;
            """;
        AddParameter(command, "Id", customerId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapCustomer(reader);
    }

    private static async Task<(decimal TotalInvoicedSyp, decimal TotalInvoicedUsd, decimal TotalPaidSyp, decimal TotalPaidUsd, decimal OutstandingSyp, decimal OutstandingUsd)> GetSummaryAsync(
        DbConnection connection,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE((SELECT SUM(total_syp) FROM invoices WHERE customer_id = @Id AND status = 'POSTED'), 0) AS total_invoiced_syp,
                COALESCE((SELECT SUM(total_usd) FROM invoices WHERE customer_id = @Id AND status = 'POSTED'), 0) AS total_invoiced_usd,
                COALESCE((SELECT SUM(amount_syp) FROM payments WHERE customer_id = @Id AND is_reversed = FALSE), 0) AS total_paid_syp,
                COALESCE((SELECT SUM(amount_usd) FROM payments WHERE customer_id = @Id AND is_reversed = FALSE), 0) AS total_paid_usd
            ;
            """;
        AddParameter(command, "Id", customerId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        var invoicedSyp = reader.GetDecimal(reader.GetOrdinal("total_invoiced_syp"));
        var invoicedUsd = reader.GetDecimal(reader.GetOrdinal("total_invoiced_usd"));
        var paidSyp = reader.GetDecimal(reader.GetOrdinal("total_paid_syp"));
        var paidUsd = reader.GetDecimal(reader.GetOrdinal("total_paid_usd"));
        return (invoicedSyp, invoicedUsd, paidSyp, paidUsd, invoicedSyp - paidSyp, invoicedUsd - paidUsd);
    }

    private static async Task<IReadOnlyCollection<CustomerStatementTransactionDto>> GetTransactionsAsync(DbConnection connection, Guid customerId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, transaction_type, occurred_at, due_date, debit_syp, credit_syp, debit_usd, credit_usd, balance_syp, balance_usd
            FROM (
                SELECT
                    id,
                    'INVOICE' AS transaction_type,
                    invoice_date::timestamp without time zone AS occurred_at,
                    due_date::timestamp without time zone AS due_date,
                    total_syp AS debit_syp,
                    0::numeric(18,4) AS credit_syp,
                    total_usd AS debit_usd,
                    0::numeric(18,4) AS credit_usd,
                    balance_syp,
                    balance_usd
                FROM invoices
                WHERE customer_id = @Id AND status = 'POSTED'

                UNION ALL

                SELECT
                    id,
                    'PAYMENT' AS transaction_type,
                    payment_date::timestamp without time zone AS occurred_at,
                    NULL AS due_date,
                    0::numeric(18,4) AS debit_syp,
                    amount_syp AS credit_syp,
                    0::numeric(18,4) AS debit_usd,
                    amount_usd AS credit_usd,
                    amount_syp - allocated_syp AS balance_syp,
                    amount_usd - allocated_usd AS balance_usd
                FROM payments
                WHERE customer_id = @Id AND is_reversed = FALSE

                UNION ALL

                SELECT
                    pa.id,
                    'ALLOCATION' AS transaction_type,
                    pa.allocation_date::timestamp without time zone AS occurred_at,
                    NULL AS due_date,
                    0::numeric(18,4) AS debit_syp,
                    pa.allocated_syp AS credit_syp,
                    0::numeric(18,4) AS debit_usd,
                    pa.allocated_usd AS credit_usd,
                    0::numeric(18,4) AS balance_syp,
                    0::numeric(18,4) AS balance_usd
                FROM payment_allocations pa
                INNER JOIN payments p ON p.id = pa.payment_id
                WHERE p.customer_id = @Id
            ) AS transactions
            ORDER BY occurred_at, transaction_type;
            """;
        AddParameter(command, "Id", customerId);

        var items = new List<CustomerStatementTransactionDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var dueDate = reader.IsDBNull(reader.GetOrdinal("due_date"))
                ? (DateTime?)null
                : reader.GetDateTime(reader.GetOrdinal("due_date"));
            var occurredAt = reader.GetDateTime(reader.GetOrdinal("occurred_at"));
            items.Add(new CustomerStatementTransactionDto(
                reader.GetGuid(reader.GetOrdinal("id")),
                reader.GetString(reader.GetOrdinal("transaction_type")),
                occurredAt,
                dueDate,
                reader.GetDecimal(reader.GetOrdinal("debit_syp")),
                reader.GetDecimal(reader.GetOrdinal("credit_syp")),
                reader.GetDecimal(reader.GetOrdinal("debit_usd")),
                reader.GetDecimal(reader.GetOrdinal("credit_usd")),
                reader.GetDecimal(reader.GetOrdinal("balance_syp")),
                reader.GetDecimal(reader.GetOrdinal("balance_usd")),
                dueDate.HasValue
                    ? (DateOnly.FromDateTime(dueDate.Value) < DateOnly.FromDateTime(DateTime.UtcNow)
                        ? $"متأخر {(DateTime.UtcNow - dueDate.Value).HumanizeAr()}"
                        : new DateTimeOffset(dueDate.Value, TimeSpan.Zero).HumanizeAr())
                    : string.Empty));
        }

        return items;
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

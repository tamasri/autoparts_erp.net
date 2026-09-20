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
                COALESCE((SELECT SUM(total_syp) FROM invoices WHERE customer_id = @Id AND status IN ('POSTED', 'VOID')), 0) AS total_invoiced_syp,
                COALESCE((SELECT SUM(total_usd) FROM invoices WHERE customer_id = @Id AND status IN ('POSTED', 'VOID')), 0) AS total_invoiced_usd,
                COALESCE((SELECT SUM(CASE WHEN payment_type = 'REFUND' THEN -amount_syp ELSE amount_syp END) FROM payments WHERE customer_id = @Id AND is_reversed = FALSE), 0) AS total_paid_syp,
                COALESCE((SELECT SUM(CASE WHEN payment_type = 'REFUND' THEN -amount_usd ELSE amount_usd END) FROM payments WHERE customer_id = @Id AND is_reversed = FALSE), 0) AS total_paid_usd
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
        // A real account statement: one row per ledger movement (posted invoices, returns, receipts, refunds) with a running
        // balance in both currencies. Payment allocations are not ledger movements (the receipt already credited the account).
        command.CommandText = """
            SELECT id, transaction_type, occurred_at, due_date, debit_syp, credit_syp, debit_usd, credit_usd,
                   SUM(debit_syp - credit_syp) OVER (ORDER BY occurred_at, created_at, id) AS balance_syp,
                   SUM(debit_usd - credit_usd) OVER (ORDER BY occurred_at, created_at, id) AS balance_usd,
                   reference
            FROM (
                SELECT i.id,
                       CASE WHEN i.invoice_type = 'RETURN' THEN 'RETURN'
                            WHEN i.invoice_type = 'CREDIT_NOTE' THEN 'CREDIT_NOTE'
                            WHEN i.status = 'VOID' THEN 'VOIDED'
                            ELSE 'INVOICE' END AS transaction_type,
                       i.invoice_date::timestamp without time zone AS occurred_at,
                       i.created_at AS created_at,
                       i.due_date::timestamp without time zone AS due_date,
                       GREATEST(i.total_syp, 0)::numeric(18,4) AS debit_syp,
                       GREATEST(-i.total_syp, 0)::numeric(18,4) AS credit_syp,
                       GREATEST(i.total_usd, 0)::numeric(18,4) AS debit_usd,
                       GREATEST(-i.total_usd, 0)::numeric(18,4) AS credit_usd,
                       COALESCE(i.invoice_number, '') AS reference
                FROM invoices i
                -- A voided invoice stays as a debit and its credit note as the offsetting credit, so the pair nets to zero.
                WHERE i.customer_id = @Id AND i.status IN ('POSTED', 'VOID')

                UNION ALL

                SELECT p.id,
                       CASE WHEN p.payment_type = 'REFUND' THEN 'REFUND' ELSE 'PAYMENT' END AS transaction_type,
                       p.payment_date::timestamp without time zone AS occurred_at,
                       p.created_at AS created_at,
                       NULL::timestamp without time zone AS due_date,
                       CASE WHEN p.payment_type = 'REFUND' THEN p.amount_syp ELSE 0 END::numeric(18,4) AS debit_syp,
                       CASE WHEN p.payment_type = 'REFUND' THEN 0 ELSE p.amount_syp END::numeric(18,4) AS credit_syp,
                       CASE WHEN p.payment_type = 'REFUND' THEN p.amount_usd ELSE 0 END::numeric(18,4) AS debit_usd,
                       CASE WHEN p.payment_type = 'REFUND' THEN 0 ELSE p.amount_usd END::numeric(18,4) AS credit_usd,
                       COALESCE(p.payment_number, '') AS reference
                FROM payments p
                WHERE p.customer_id = @Id AND p.is_reversed = FALSE
            ) AS transactions
            ORDER BY occurred_at, created_at, id;
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
                    : string.Empty,
                reader.GetString(reader.GetOrdinal("reference"))));
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

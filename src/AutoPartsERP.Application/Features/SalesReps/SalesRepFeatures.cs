namespace AutoPartsERP.Application.Features.SalesReps;

// Sales representatives: a rep is a user with a row in sales_reps. Anyone holding sales_reps:read sees every rep; a rep without it sees
// only their own figures. Only sales_reps:manage adds reps, changes their terms and hands customers over.

/// <summary>Who may see which reps: everyone (null), only this user, or nobody.</summary>
internal static class SalesRepScope
{
    public static async Task<Result<Guid?>> ResolveAsync(DbConnection connection, ICurrentUser user, CancellationToken cancellationToken)
    {
        if (user.HasPermission(PermissionCodes.SalesReps.Read))
        {
            return Result<Guid?>.Success(null);
        }

        var isRep = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM sales_reps WHERE user_id = @UserId);", new { user.UserId }, cancellationToken: cancellationToken));
        return isRep
            ? Result<Guid?>.Success(user.UserId)
            : Result<Guid?>.Failure(new Error("Authorization.Forbidden", "You do not have permission to view sales representatives."));
    }
}

internal static class SalesRepReader
{
    /// <summary>
    /// One row per posted sale or return of a rep: its total, balance, revenue (after line and invoice discounts, without delivery fees
    /// and tax) and gross profit (revenue minus the cost of the goods at the time of sale). A return counts negative.
    /// </summary>
    public const string InvoiceFigures = """
        SELECT i.id, i.sales_rep_id AS rep, i.invoice_type AS kind, i.invoice_date AS day, i.total_usd AS total, i.balance_usd AS balance,
               i.total_usd - s.sign * (i.delivery_fee_usd + i.tax_amount_usd) AS revenue,
               i.total_usd - s.sign * (i.delivery_fee_usd + i.tax_amount_usd) - s.sign * c.cost AS gross_profit
        FROM invoices i
        CROSS JOIN LATERAL (SELECT CASE WHEN i.invoice_type = 'RETURN' THEN -1 ELSE 1 END AS sign) s
        CROSS JOIN LATERAL (SELECT COALESCE(sum(l.quantity * l.cost_price_usd), 0) AS cost FROM invoice_lines l WHERE l.invoice_id = i.id) c
        WHERE i.status = 'POSTED' AND i.invoice_type IN ('SALE', 'RETURN') AND i.sales_rep_id IS NOT NULL
        """;

    private sealed record Row(
        Guid UserId, string UserName, string FullName, string? Phone, decimal CommissionPct, decimal MonthlyTargetUsd, bool IsActive, string? Notes,
        int CustomerCount, int InvoiceCount, decimal SalesUsd, decimal ReturnsUsd, decimal RevenueUsd, decimal GrossProfitUsd, decimal CollectedUsd,
        decimal OutstandingUsd);

    /// <summary>Posted sales and returns only; see <see cref="SalesRepDto"/> for what each figure means.</summary>
    public static async Task<IReadOnlyList<SalesRepDto>> ListAsync(
        DbConnection connection, DateOnly from, DateOnly to, Guid? onlyUser, bool includeInactive, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"""
            WITH inv AS ({InvoiceFigures}
            ), period AS (
                SELECT rep, count(*) FILTER (WHERE kind = 'SALE') AS invoices,
                       COALESCE(sum(total) FILTER (WHERE kind = 'SALE'), 0) AS sales,
                       COALESCE(-sum(total) FILTER (WHERE kind = 'RETURN'), 0) AS returns,
                       COALESCE(sum(revenue), 0) AS revenue,
                       COALESCE(sum(gross_profit), 0) AS gross_profit
                FROM inv WHERE day BETWEEN @from AND @to GROUP BY rep
            ), owed AS (
                SELECT rep, sum(balance) AS outstanding FROM inv WHERE kind = 'SALE' GROUP BY rep
            ), collected AS (
                SELECT i.sales_rep_id AS rep, sum(a.allocated_usd) AS amount
                FROM payment_allocations a
                INNER JOIN payments p ON p.id = a.payment_id AND NOT p.is_reversed
                INNER JOIN invoices i ON i.id = a.invoice_id
                WHERE i.sales_rep_id IS NOT NULL AND a.allocation_date BETWEEN @from AND @to
                GROUP BY i.sales_rep_id
            ), cust AS (
                SELECT assigned_sales_rep AS rep, count(*) AS n FROM customers WHERE assigned_sales_rep IS NOT NULL AND is_active GROUP BY 1
            )
            SELECT r.user_id AS UserId, u.user_name AS UserName, COALESCE(NULLIF(u.full_name, ''), u.user_name) AS FullName, u.phone_number AS Phone,
                   r.commission_pct AS CommissionPct, r.monthly_target_usd AS MonthlyTargetUsd, r.is_active AS IsActive, r.notes AS Notes,
                   COALESCE(c.n, 0)::int AS CustomerCount, COALESCE(p.invoices, 0)::int AS InvoiceCount,
                   COALESCE(p.sales, 0) AS SalesUsd, COALESCE(p.returns, 0) AS ReturnsUsd, COALESCE(p.revenue, 0) AS RevenueUsd,
                   COALESCE(p.gross_profit, 0) AS GrossProfitUsd,
                   COALESCE(col.amount, 0) AS CollectedUsd, COALESCE(o.outstanding, 0) AS OutstandingUsd
            FROM sales_reps r
            INNER JOIN asp_net_users u ON u.id = r.user_id
            LEFT JOIN period p ON p.rep = r.user_id
            LEFT JOIN owed o ON o.rep = r.user_id
            LEFT JOIN collected col ON col.rep = r.user_id
            LEFT JOIN cust c ON c.rep = r.user_id
            WHERE (@includeInactive OR r.is_active) AND (@onlyUser::uuid IS NULL OR r.user_id = @onlyUser)
            ORDER BY COALESCE(p.sales, 0) - COALESCE(p.returns, 0) DESC, FullName;
            """,
            new { from, to, onlyUser, includeInactive }, cancellationToken: cancellationToken));

        return rows.Select(r =>
        {
            var net = r.SalesUsd - r.ReturnsUsd;
            var target = SalesRepMetrics.Target(r.MonthlyTargetUsd, from, to);
            return new SalesRepDto(
                r.UserId, r.UserName, r.FullName, r.Phone, r.CommissionPct, r.MonthlyTargetUsd, r.IsActive, r.Notes,
                r.CustomerCount, r.InvoiceCount, r.SalesUsd, r.ReturnsUsd, net, r.GrossProfitUsd, SalesRepMetrics.Margin(r.GrossProfitUsd, r.RevenueUsd),
                r.CollectedUsd, r.OutstandingUsd, SalesRepMetrics.Commission(r.GrossProfitUsd, r.CommissionPct), target, SalesRepMetrics.Achievement(net, target),
                r.InvoiceCount == 0 ? 0 : Math.Round(r.SalesUsd / r.InvoiceCount, 2), SalesRepMetrics.Share(r.ReturnsUsd, r.SalesUsd),
                SalesRepMetrics.Share(r.CollectedUsd, net));
        }).ToList();
    }
}

// ---------------------------------------------------------------- list / detail

public sealed record GetSalesRepsQuery(DateOnly? From, DateOnly? To, bool IncludeInactive) : IRequest<Result<IReadOnlyList<SalesRepDto>>>;

public sealed class GetSalesRepsQueryHandler : IRequestHandler<GetSalesRepsQuery, Result<IReadOnlyList<SalesRepDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public GetSalesRepsQueryHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<SalesRepDto>>> Handle(GetSalesRepsQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var scope = await SalesRepScope.ResolveAsync(connection, _currentUser, cancellationToken);
        if (scope.IsFailure)
        {
            return Result<IReadOnlyList<SalesRepDto>>.Failure(scope.Error);
        }

        var (from, to) = Period(request.From, request.To);
        return Result<IReadOnlyList<SalesRepDto>>.Success(
            await SalesRepReader.ListAsync(connection, from, to, scope.Value, request.IncludeInactive || scope.Value is not null, cancellationToken));
    }

    internal static (DateOnly From, DateOnly To) Period(DateOnly? from, DateOnly? to)
    {
        var fallback = SalesRepMetrics.DefaultPeriod(DateOnly.FromDateTime(DateTime.Today));
        return (from ?? fallback.From, to ?? fallback.To);
    }
}

public sealed record GetSalesRepDetailQuery(Guid UserId, DateOnly? From, DateOnly? To) : IRequest<Result<SalesRepDetailDto>>;

public sealed class GetSalesRepDetailQueryHandler : IRequestHandler<GetSalesRepDetailQuery, Result<SalesRepDetailDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public GetSalesRepDetailQueryHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<SalesRepDetailDto>> Handle(GetSalesRepDetailQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var scope = await SalesRepScope.ResolveAsync(connection, _currentUser, cancellationToken);
        if (scope.IsFailure || (scope.Value is { } self && self != request.UserId))
        {
            return Result<SalesRepDetailDto>.Failure(scope.IsFailure ? scope.Error : new Error("Authorization.Forbidden", "You can only see your own figures."));
        }

        var (from, to) = GetSalesRepsQueryHandler.Period(request.From, request.To);
        var rep = (await SalesRepReader.ListAsync(connection, from, to, request.UserId, true, cancellationToken)).FirstOrDefault();
        if (rep is null)
        {
            return Result<SalesRepDetailDto>.Failure(new Error("SalesRep.NotFound", "Sales representative was not found."));
        }

        // Twelve months ending with the period's last month, so a trend is visible whatever period is chosen: each month's target, what was
        // achieved against it, its gross profit and the commission that earned.
        var firstMonth = new DateOnly(to.Year, to.Month, 1).AddMonths(-11);
        var months = (await connection.QueryAsync<(int Year, int Month, decimal NetSalesUsd, decimal GrossProfitUsd, decimal CollectedUsd)>(new CommandDefinition(
            $"""
            WITH inv AS ({SalesRepReader.InvoiceFigures} AND i.sales_rep_id = @UserId),
                 m AS (SELECT generate_series(@firstMonth::date, date_trunc('month', @to::date)::date, interval '1 month')::date AS month)
            SELECT extract(year FROM m.month)::int AS Year, extract(month FROM m.month)::int AS Month,
                   COALESCE((SELECT sum(total) FROM inv WHERE date_trunc('month', day)::date = m.month), 0) AS NetSalesUsd,
                   COALESCE((SELECT sum(gross_profit) FROM inv WHERE date_trunc('month', day)::date = m.month), 0) AS GrossProfitUsd,
                   COALESCE((SELECT sum(a.allocated_usd) FROM payment_allocations a
                             INNER JOIN payments p ON p.id = a.payment_id AND NOT p.is_reversed
                             INNER JOIN invoices i ON i.id = a.invoice_id
                             WHERE i.sales_rep_id = @UserId AND date_trunc('month', a.allocation_date)::date = m.month), 0) AS CollectedUsd
            FROM m ORDER BY m.month;
            """,
            new { request.UserId, firstMonth, to }, cancellationToken: cancellationToken)))
            .Select(m => new SalesRepMonthDto(m.Year, m.Month, rep.MonthlyTargetUsd, m.NetSalesUsd, m.GrossProfitUsd,
                SalesRepMetrics.Commission(m.GrossProfitUsd, rep.CommissionPct), m.CollectedUsd))
            .ToList();

        var customers = (await connection.QueryAsync<SalesRepCustomerDto>(new CommandDefinition(
            """
            SELECT c.id AS Id, c.code AS Code, c.name AS Name, c.phone AS Phone,
                   COALESCE((SELECT sum(i.balance_usd) FROM invoices i WHERE i.customer_id = c.id AND i.status = 'POSTED' AND i.invoice_type = 'SALE'), 0) AS OutstandingUsd,
                   (SELECT max(i.invoice_date) FROM invoices i WHERE i.customer_id = c.id AND i.status = 'POSTED') AS LastInvoiceDate
            FROM customers c
            WHERE c.assigned_sales_rep = @UserId AND c.is_active
            ORDER BY c.name;
            """,
            new { request.UserId }, cancellationToken: cancellationToken))).ToList();

        var invoices = (await connection.QueryAsync<SalesRepInvoiceDto>(new CommandDefinition(
            $"""
            WITH inv AS ({SalesRepReader.InvoiceFigures} AND i.sales_rep_id = @UserId AND i.invoice_date BETWEEN @from AND @to)
            SELECT i.id AS Id, i.invoice_number AS InvoiceNumber, i.invoice_type AS Type, i.invoice_date AS InvoiceDate, c.name AS CustomerName,
                   i.total_usd AS TotalUsd, i.balance_usd AS BalanceUsd, inv.gross_profit AS GrossProfitUsd
            FROM inv INNER JOIN invoices i ON i.id = inv.id INNER JOIN customers c ON c.id = i.customer_id
            ORDER BY i.invoice_date DESC, i.serial_no DESC
            LIMIT 500;
            """,
            new { request.UserId, from, to }, cancellationToken: cancellationToken))).ToList();

        return Result<SalesRepDetailDto>.Success(new SalesRepDetailDto(rep, months, customers, invoices));
    }
}

public sealed record GetSalesRepCandidatesQuery : IRequest<Result<IReadOnlyList<SalesRepCandidateDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.SalesReps.Manage;
}

public sealed class GetSalesRepCandidatesQueryHandler : IRequestHandler<GetSalesRepCandidatesQuery, Result<IReadOnlyList<SalesRepCandidateDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetSalesRepCandidatesQueryHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result<IReadOnlyList<SalesRepCandidateDto>>> Handle(GetSalesRepCandidatesQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<SalesRepCandidateDto>(new CommandDefinition(
            """
            SELECT u.id AS UserId, u.user_name AS UserName, COALESCE(NULLIF(u.full_name, ''), u.user_name) AS FullName
            FROM asp_net_users u
            WHERE NOT EXISTS (SELECT 1 FROM sales_reps r WHERE r.user_id = u.id)
              AND (u.lockout_end IS NULL OR u.lockout_end < now())
            ORDER BY FullName;
            """,
            cancellationToken: cancellationToken));
        return Result<IReadOnlyList<SalesRepCandidateDto>>.Success(rows.ToList());
    }
}

// ---------------------------------------------------------------- manage

/// <summary>Makes a user a rep, or changes a rep's terms. Deactivating keeps their history; they can no longer be put on a new invoice.</summary>
public sealed record SaveSalesRepCommand(Guid UserId, SaveSalesRepRequest Request) : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.SalesReps.Manage;
    public string AuditModule => "SALES";
}

public sealed class SaveSalesRepCommandValidator : AbstractValidator<SaveSalesRepCommand>
{
    public SaveSalesRepCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Request.CommissionPct).InclusiveBetween(0, 100);
        RuleFor(x => x.Request.MonthlyTargetUsd).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.Notes).MaximumLength(1000);
    }
}

public sealed class SaveSalesRepCommandHandler : IRequestHandler<SaveSalesRepCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public SaveSalesRepCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(SaveSalesRepCommand command, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        if (!await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM asp_net_users WHERE id = @UserId);", new { command.UserId }, cancellationToken: cancellationToken)))
        {
            return Result<Guid>.Failure(new Error("Users.NotFound", "User was not found."));
        }

        var r = command.Request;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO sales_reps (user_id, commission_pct, monthly_target_usd, is_active, notes, created_by)
            VALUES (@UserId, @CommissionPct, @MonthlyTargetUsd, @IsActive, @Notes, @By)
            ON CONFLICT (user_id) DO UPDATE
            SET commission_pct = EXCLUDED.commission_pct, monthly_target_usd = EXCLUDED.monthly_target_usd, is_active = EXCLUDED.is_active,
                notes = EXCLUDED.notes, updated_at = now(), updated_by = @By;
            """,
            new { command.UserId, r.CommissionPct, r.MonthlyTargetUsd, r.IsActive, Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim(), By = _currentUser.UserId },
            cancellationToken: cancellationToken));
        return Result<Guid>.Success(command.UserId);
    }
}

/// <summary>Hands customers to a rep. New invoices for them default to this rep; invoices already made keep the rep they had.</summary>
public sealed record AssignSalesRepCustomersCommand(Guid UserId, AssignSalesRepCustomersRequest Request) : IRequest<Result<int>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.SalesReps.Manage;
    public string AuditModule => "SALES";
}

public sealed class AssignSalesRepCustomersCommandValidator : AbstractValidator<AssignSalesRepCustomersCommand>
{
    public AssignSalesRepCustomersCommandValidator()
    {
        RuleFor(x => x.Request.CustomerIds).NotEmpty().Must(ids => ids.Count <= 500).WithMessage("Up to 500 customers at a time.");
    }
}

public sealed class AssignSalesRepCustomersCommandHandler : IRequestHandler<AssignSalesRepCustomersCommand, Result<int>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public AssignSalesRepCustomersCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<int>> Handle(AssignSalesRepCustomersCommand command, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        if (!await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM sales_reps WHERE user_id = @UserId AND is_active);", new { command.UserId }, cancellationToken: cancellationToken)))
        {
            return Result<int>.Failure(new Error("SalesRep.NotActive", "Customers can only be given to an active sales representative."));
        }

        var ids = command.Request.CustomerIds.Distinct().ToArray();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE customers SET assigned_sales_rep = @UserId, updated_at = now(), updated_by = @By WHERE id = ANY(@ids);",
            new { command.UserId, ids, By = _currentUser.UserId }, transaction, cancellationToken: cancellationToken));
        if (updated != ids.Length)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<int>.Failure(new Error("Customer.NotFound", $"{ids.Length - updated} of the customers were not found; nothing was changed."));
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<int>.Success(updated);
    }
}

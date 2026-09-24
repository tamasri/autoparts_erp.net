using AutoPartsERP.Application.Common.Pricing;
using Dapper;

namespace AutoPartsERP.Application.Features.Invoices.Returns;

// Sales returns (مردود مبيعات), as ERPNext's Sales Invoice with is_return + return_against: made from a posted sale, line by line,
// at the sale's own prices, line discounts, exchange rate and cost, with the sale's share of the invoice discount, never more than
// the sale still holds. The return then follows the invoice steps (confirm, post with approval); posting brings the goods back at
// the cost they were sold at and credits the sale (PostInvoiceCommand).

public sealed record GetSalesReturnableQuery(Guid InvoiceId) : IRequest<Result<IReadOnlyList<ReturnableLineDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.Read;
}

public sealed class GetSalesReturnableQueryHandler : IRequestHandler<GetSalesReturnableQuery, Result<IReadOnlyList<ReturnableLineDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetSalesReturnableQueryHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result<IReadOnlyList<ReturnableLineDto>>> Handle(GetSalesReturnableQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return Result<IReadOnlyList<ReturnableLineDto>>.Success(await SalesReturnLines.ReadAsync(connection, null, request.InvoiceId, cancellationToken));
    }
}

public sealed record CreateSalesReturnCommand(Guid InvoiceId, CreateReturnRequest Request, string IdempotencyKey)
    : IRequest<Result<InvoiceDto>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest, IPeriodSensitiveRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.Create;
    public string AuditModule => "INVOICES";
    public DateTimeOffset OperationDate => Request.ReturnDate.ToDateTime(TimeOnly.MinValue);
    public string Module => InvoicePeriod.Module;
}

public sealed class CreateSalesReturnCommandValidator : AbstractValidator<CreateSalesReturnCommand>
{
    public CreateSalesReturnCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Request.Reason).MaximumLength(500);
        RuleFor(x => x.Request.Lines).NotEmpty().WithMessage("Choose at least one line to return.");
        RuleForEach(x => x.Request.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.LineId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0);
        });
        RuleFor(x => x.Request.Lines).Must(l => l.Select(x => x.LineId).Distinct().Count() == l.Count).WithMessage("Each line of the invoice once.");
    }
}

public sealed class CreateSalesReturnCommandHandler : IRequestHandler<CreateSalesReturnCommand, Result<InvoiceDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreateSalesReturnCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    private sealed record Sale(
        Guid Id, string Type, string Status, DateOnly InvoiceDate, decimal SubtotalSyp, decimal SubtotalUsd, decimal DiscountAmountSyp, decimal DiscountAmountUsd);

    private sealed record SourceLine(Guid Id, decimal UnitPriceSyp, decimal DiscountPct);

    public async Task<Result<InvoiceDto>> Handle(CreateSalesReturnCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var sale = await connection.QuerySingleOrDefaultAsync<Sale>(new CommandDefinition(
            """
            SELECT id AS Id, invoice_type AS Type, status AS Status, invoice_date AS InvoiceDate, subtotal_syp AS SubtotalSyp, subtotal_usd AS SubtotalUsd,
                   discount_amount_syp AS DiscountAmountSyp, discount_amount_usd AS DiscountAmountUsd
            FROM invoices WHERE id = @InvoiceId FOR UPDATE;
            """,
            new { command.InvoiceId }, transaction, cancellationToken: cancellationToken));
        if (sale is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<InvoiceDto>.Failure(new Error("Invoice.NotFound", "Invoice was not found."));
        }

        if (sale.Type != "SALE" || sale.Status != "POSTED")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<InvoiceDto>.Failure(new Error("SalesReturn.NotReturnable", "Only a posted sales invoice can be returned."));
        }

        if (request.ReturnDate < sale.InvoiceDate)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<InvoiceDto>.Failure(new Error("SalesReturn.BeforeSale", "A return cannot be dated before the invoice it returns."));
        }

        var returnable = (await SalesReturnLines.ReadAsync(connection, transaction, sale.Id, cancellationToken)).ToDictionary(l => l.LineId);
        foreach (var line in request.Lines)
        {
            if (!returnable.TryGetValue(line.LineId, out var source))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<InvoiceDto>.Failure(new Error("SalesReturn.LineNotOnInvoice", "A chosen line is not a line of this invoice."));
            }

            if (line.Quantity > source.Returnable)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<InvoiceDto>.Failure(new Error("SalesReturn.ExceedsSold", $"Item {source.Code}: at most {source.Returnable:0.####} can still be returned."));
            }
        }

        var locations = request.Lines.Select(l => l.LocationId).OfType<Guid>().Distinct().ToArray();
        if (locations.Length > 0 && await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT count(*) FROM locations WHERE id = ANY(@locations) AND is_active;", new { locations }, transaction, cancellationToken: cancellationToken)) != locations.Length)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<InvoiceDto>.Failure(new Error("SalesReturn.LocationNotFound", "A chosen location does not exist or is inactive."));
        }

        // The sale's discount is given back in proportion to the returned lines (all of it once everything is back), in both currencies.
        var sourceLines = (await connection.QueryAsync<SourceLine>(new CommandDefinition(
            "SELECT id AS Id, unit_price_syp AS UnitPriceSyp, discount_pct AS DiscountPct FROM invoice_lines WHERE invoice_id = @Id;",
            new { sale.Id }, transaction, cancellationToken: cancellationToken))).ToDictionary(l => l.Id);
        var returnedUsd = request.Lines.Sum(l => l.Quantity * returnable[l.LineId].UnitPriceUsd * (1 - returnable[l.LineId].DiscountPct / 100m));
        var returnedSyp = request.Lines.Sum(l => l.Quantity * sourceLines[l.LineId].UnitPriceSyp * (1 - sourceLines[l.LineId].DiscountPct / 100m));
        var given = await connection.QuerySingleAsync<(decimal Syp, decimal Usd)>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(discount_amount_syp), 0) AS Syp, COALESCE(SUM(discount_amount_usd), 0) AS Usd
            FROM invoices WHERE original_invoice_id = @Id AND invoice_type = 'RETURN' AND status = 'POSTED';
            """,
            new { sale.Id }, transaction, cancellationToken: cancellationToken));
        var everythingLeft = returnable.Values.All(s => s.Returnable == (request.Lines.FirstOrDefault(l => l.LineId == s.LineId)?.Quantity ?? 0m));
        var discountUsd = DocumentDiscount.ReturnShare(sale.DiscountAmountUsd, sale.SubtotalUsd, returnedUsd, given.Usd, everythingLeft);
        var discountSyp = DocumentDiscount.ReturnShare(sale.DiscountAmountSyp, sale.SubtotalSyp, returnedSyp, given.Syp, everythingLeft);

        var returnId = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO invoices (
                id, invoice_type, status, customer_id, invoice_date, due_date, original_invoice_id,
                subtotal_syp, subtotal_usd, discount_pct, discount_amount_syp, discount_amount_usd,
                delivery_fee_syp, delivery_fee_usd, tax_amount_syp, tax_amount_usd, total_syp, total_usd,
                paid_syp, paid_usd, fx_rate_id, fx_rate_snapshot, sales_rep_id, notes, created_at, created_by)
            SELECT @ReturnId, 'RETURN', 'DRAFT', customer_id, @ReturnDate, @ReturnDate, id,
                   0, 0, NULL, @DiscountSyp, @DiscountUsd, 0, 0, 0, 0, 0, 0,
                   0, 0, fx_rate_id, fx_rate_snapshot, sales_rep_id, @Reason, now(), @By
            FROM invoices WHERE id = @SaleId;
            """,
            new
            {
                ReturnId = returnId, request.ReturnDate, DiscountSyp = discountSyp, DiscountUsd = discountUsd, Reason = request.Reason?.Trim(),
                By = _currentUser.UserId, SaleId = sale.Id
            },
            transaction, cancellationToken: cancellationToken));

        var number = 0;
        foreach (var line in request.Lines)
        {
            // Same SKU, batch, prices, line discount and cost as sold; the goods come back to the chosen location or where they left from.
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO invoice_lines (
                    id, invoice_id, line_number, sku_id, batch_id, location_id, description, quantity,
                    unit_price_syp, unit_price_usd, discount_pct, cost_price_syp, cost_price_usd, fx_rate_used,
                    is_price_override, price_override_reason, return_of_line_id, created_at)
                SELECT uuid_generate_v4(), @ReturnId, @n, o.sku_id, o.batch_id, COALESCE(@LocationId, o.location_id), o.description, @Quantity,
                       o.unit_price_syp, o.unit_price_usd, o.discount_pct, o.cost_price_syp, o.cost_price_usd, o.fx_rate_used,
                       FALSE, NULL, o.id, now()
                FROM invoice_lines o WHERE o.id = @LineId;
                """,
                new { ReturnId = returnId, n = ++number, line.LocationId, line.Quantity, line.LineId }, transaction, cancellationToken: cancellationToken));
        }

        await InvoiceTotals.RecalculateAsync(connection, transaction, returnId, cancellationToken);
        var dto = await InvoiceReader.LoadAsync(connection, transaction, returnId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<InvoiceDto>.Success(dto!);
    }
}

/// <summary>A sale's lines with what posted returns took back and what may still be returned.</summary>
internal static class SalesReturnLines
{
    public static async Task<IReadOnlyList<ReturnableLineDto>> ReadAsync(DbConnection connection, DbTransaction? transaction, Guid invoiceId, CancellationToken ct) =>
        (await connection.QueryAsync<ReturnableLineDto>(new CommandDefinition(
            """
            SELECT l.id AS LineId, l.line_number AS LineNumber, s.code AS Code, COALESCE(NULLIF(s.name_ar, ''), s.name) AS Name,
                   l.quantity AS Quantity, r.returned AS Returned, l.quantity - r.returned AS Returnable,
                   l.unit_price_usd AS UnitPriceUsd, l.discount_pct AS DiscountPct, l.location_id AS LocationId
            FROM invoice_lines l
            INNER JOIN skus s ON s.id = l.sku_id
            CROSS JOIN LATERAL (
                SELECT COALESCE(SUM(x.quantity), 0) AS returned FROM invoice_lines x
                INNER JOIN invoices xi ON xi.id = x.invoice_id
                WHERE x.return_of_line_id = l.id AND xi.status = 'POSTED') r
            WHERE l.invoice_id = @invoiceId
            ORDER BY l.line_number;
            """,
            new { invoiceId }, transaction, cancellationToken: ct))).ToList();

    /// <summary>The first item a return would take beyond what its sale still holds (every posted return counted), or null.</summary>
    public static Task<string?> OverReturnedAsync(DbConnection connection, DbTransaction transaction, Guid returnId, CancellationToken ct) =>
        connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT s.code
            FROM invoice_lines r
            INNER JOIN invoice_lines o ON o.id = r.return_of_line_id
            INNER JOIN skus s ON s.id = o.sku_id
            WHERE r.invoice_id = @returnId
              AND r.quantity > o.quantity - (SELECT COALESCE(SUM(x.quantity), 0) FROM invoice_lines x
                                              INNER JOIN invoices xi ON xi.id = x.invoice_id
                                              WHERE x.return_of_line_id = o.id AND xi.status = 'POSTED')
            LIMIT 1;
            """,
            new { returnId }, transaction, cancellationToken: ct));
}

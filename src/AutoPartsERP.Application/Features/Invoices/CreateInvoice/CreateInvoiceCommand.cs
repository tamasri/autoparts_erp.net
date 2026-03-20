using System.Data.Common;
using Dapper;
using Humanizer;

namespace AutoPartsERP.Application.Features.Invoices.CreateInvoice;

public sealed record CreateInvoiceCommand(
    Guid CustomerId,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    Guid FxRateId,
    Guid? SalesRepId,
    string InvoiceType,
    decimal DeliveryFeeSyp,
    decimal DeliveryFeeUsd,
    IReadOnlyCollection<CreateInvoiceLineRequest> Lines,
    string IdempotencyKey)
    : IRequest<Result<InvoiceDto>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.Create;
    public string AuditModule => "INVOICES";
}

public sealed class CreateInvoiceCommandValidator : AbstractValidator<CreateInvoiceCommand>
{
    public CreateInvoiceCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.FxRateId).NotEmpty();
        RuleFor(x => x.InvoiceType).NotEmpty();
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.InvoiceDate);
        RuleFor(x => x.DeliveryFeeSyp).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DeliveryFeeUsd).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Lines).NotNull().Must(x => x.Count > 0).WithMessage("At least one invoice line is required.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.SkuId).NotEmpty();
            line.RuleFor(x => x.LocationId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.UnitPriceSyp).GreaterThanOrEqualTo(0);
            line.RuleFor(x => x.UnitPriceUsd).GreaterThanOrEqualTo(0);
            line.RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100);
            line.When(x => x.IsPriceOverride, () =>
            {
                line.RuleFor(x => x.OverrideReason).NotEmpty();
            });
        });
    }
}

public sealed class CreateInvoiceCommandHandler : IRequestHandler<CreateInvoiceCommand, Result<InvoiceDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreateInvoiceCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<InvoiceDto>> Handle(CreateInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var customer = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Code, string Name)>(
            new CommandDefinition(
                "SELECT id AS Id, code AS Code, name AS Name FROM customers WHERE id = @CustomerId;",
                new { request.CustomerId },
                transaction,
                cancellationToken: cancellationToken));
        if (customer.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<InvoiceDto>.Failure(new Error("Customer.NotFound", "Customer was not found."));
        }

        var fxRate = await connection.QuerySingleOrDefaultAsync<(Guid Id, decimal MidRate)>(
            new CommandDefinition(
                "SELECT id AS Id, mid_rate AS MidRate FROM fx_rates WHERE id = @FxRateId;",
                new { request.FxRateId },
                transaction,
                cancellationToken: cancellationToken));
        if (fxRate.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<InvoiceDto>.Failure(new Error("FxRate.NotFound", "FX rate was not found."));
        }

        var invoiceId = Guid.NewGuid();
        var invoiceType = request.InvoiceType.Trim().ToUpperInvariant();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO invoices (
                id, invoice_type, status, customer_id, invoice_date, due_date,
                delivery_address, subtotal_syp, subtotal_usd, discount_amount_syp, discount_amount_usd,
                delivery_fee_syp, delivery_fee_usd, tax_amount_syp, tax_amount_usd, total_syp, total_usd,
                paid_syp, paid_usd, fx_rate_id, fx_rate_snapshot, sales_rep_id, created_at, created_by)
            VALUES (
                @Id, @InvoiceType, 'DRAFT', @CustomerId, @InvoiceDate, @DueDate,
                NULL, 0, 0, 0, 0, @DeliveryFeeSyp, @DeliveryFeeUsd, 0, 0,
                @DeliveryFeeSyp, @DeliveryFeeUsd, 0, 0, @FxRateId, @FxRateSnapshot, @SalesRepId, now(), @CreatedBy);
            """,
            new
            {
                Id = invoiceId,
                InvoiceType = invoiceType,
                request.CustomerId,
                request.InvoiceDate,
                request.DueDate,
                request.DeliveryFeeSyp,
                request.DeliveryFeeUsd,
                FxRateSnapshot = fxRate.MidRate,
                request.SalesRepId,
                CreatedBy = _currentUser.UserId
            },
            transaction,
            cancellationToken: cancellationToken));

        var lineNumber = 1;
        foreach (var line in request.Lines)
        {
            var sku = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Code, string Name, string NameAr, decimal MinSellingPriceSyp, decimal MinSellingPriceUsd)>(
                new CommandDefinition(
                    "SELECT id AS Id, code AS Code, name AS Name, name_ar AS NameAr, min_selling_price_syp AS MinSellingPriceSyp, min_selling_price_usd AS MinSellingPriceUsd FROM skus WHERE id = @SkuId;",
                    new { line.SkuId },
                    transaction,
                    cancellationToken: cancellationToken));

            if (sku.Id == Guid.Empty)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<InvoiceDto>.Failure(new Error("Sku.NotFound", "SKU was not found."));
            }

            if (line.IsPriceOverride && !_currentUser.HasPermission(PermissionCodes.Invoices.PriceOverride))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<InvoiceDto>.Failure(new Error("Authorization.Forbidden", "Price override permission is required."));
            }

            if (line.IsPriceOverride && string.IsNullOrWhiteSpace(line.OverrideReason))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<InvoiceDto>.Failure(new Error("Invoice.OverrideReasonRequired", "Price override reason is required."));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO invoice_lines (
                    id, invoice_id, line_number, sku_id, batch_id, location_id, description, quantity,
                    unit_price_syp, unit_price_usd, discount_pct, cost_price_syp, cost_price_usd,
                    fx_rate_used, is_price_override, price_override_reason, created_at)
                VALUES (
                    @Id, @InvoiceId, @LineNumber, @SkuId, @BatchId, @LocationId, @Description, @Quantity,
                    @UnitPriceSyp, @UnitPriceUsd, @DiscountPct, 0, 0, @FxRateUsed, @IsPriceOverride, @OverrideReason, now());
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoiceId,
                    LineNumber = lineNumber++,
                    line.SkuId,
                    line.BatchId,
                    line.LocationId,
                    Description = sku.Name,
                    line.Quantity,
                    line.UnitPriceSyp,
                    line.UnitPriceUsd,
                    line.DiscountPct,
                    FxRateUsed = fxRate.MidRate,
                    line.IsPriceOverride,
                    OverrideReason = line.OverrideReason
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        var invoice = await LoadInvoiceAsync(connection, transaction, invoiceId, cancellationToken);
        if (invoice is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<InvoiceDto>.Failure(new Error("Invoice.NotFound", "Invoice was not found after creation."));
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<InvoiceDto>.Success(invoice);
    }

    private static async Task<InvoiceDto?> LoadInvoiceAsync(DbConnection connection, DbTransaction transaction, Guid invoiceId, CancellationToken cancellationToken)
    {
        var header = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(
                """
                SELECT
                    i.id AS Id,
                    i.invoice_number AS InvoiceNumber,
                    i.status AS Status,
                    i.invoice_type AS Type,
                    i.customer_id AS CustomerId,
                    c.code AS CustomerCode,
                    c.name AS CustomerName,
                    i.invoice_date AS InvoiceDate,
                    i.due_date AS DueDate,
                    i.total_syp AS TotalSyp,
                    i.total_usd AS TotalUsd,
                    i.paid_syp AS PaidSyp,
                    i.paid_usd AS PaidUsd
                FROM invoices i
                INNER JOIN customers c ON c.id = i.customer_id
                WHERE i.id = @InvoiceId;
                """,
                new { InvoiceId = invoiceId },
                transaction,
                cancellationToken: cancellationToken));

        if (header is null)
        {
            return null;
        }

        var lines = (await connection.QueryAsync<InvoiceLineDto>(
            new CommandDefinition(
                """
                SELECT
                    il.id AS Id,
                    il.line_number AS LineNumber,
                    il.sku_id AS SkuId,
                    s.code AS SkuCode,
                    s.name AS SkuName,
                    il.batch_id AS BatchId,
                    il.location_id AS LocationId,
                    il.quantity AS Quantity,
                    il.unit_price_syp AS UnitPriceSyp,
                    il.unit_price_usd AS UnitPriceUsd,
                    il.discount_pct AS DiscountPct,
                    il.line_total_syp AS LineTotalSyp,
                    il.line_total_usd AS LineTotalUsd,
                    il.quantity * il.cost_price_syp AS GrossMarginSyp,
                    il.quantity * il.cost_price_usd AS GrossMarginUsd,
                    CASE WHEN il.line_total_syp = 0 THEN 0 ELSE ((il.line_total_syp - (il.quantity * il.cost_price_syp)) / il.line_total_syp) * 100 END AS GrossMarginPct,
                    il.is_price_override AS IsPriceOverride,
                    il.price_override_reason AS OverrideReason
                FROM invoice_lines il
                INNER JOIN skus s ON s.id = il.sku_id
                WHERE il.invoice_id = @InvoiceId
                ORDER BY il.line_number;
                """,
                new { InvoiceId = invoiceId },
                transaction,
                cancellationToken: cancellationToken))).ToArray();

        return InvoiceMappings.ToInvoiceDto(
            header.id,
            header.invoicenumber ?? string.Empty,
            header.status,
            header.type,
            header.customerid,
            header.customercode,
            header.customername,
            header.invoicedate,
            header.duedate,
            header.totalsyp,
            header.totalusd,
            header.paidsyp,
            header.paidusd,
            lines);
    }
}

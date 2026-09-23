using System.Data.Common;
using Dapper;
using Humanizer;

using AutoPartsERP.Application.Features.SalesReps;

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
    string IdempotencyKey,
    decimal? DiscountPct = null,
    decimal? DiscountAmountUsd = null)
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
        RuleFor(x => x).Must(x => x.DiscountPct is null || x.DiscountAmountUsd is null).WithMessage("Give the invoice discount as a percentage or as an amount, not both.");
        RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100).When(x => x.DiscountPct is not null);
        RuleFor(x => x.DiscountAmountUsd).GreaterThanOrEqualTo(0).When(x => x.DiscountAmountUsd is not null);
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

        var customer = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Code, string Name, Guid? SalesRep)>(
            new CommandDefinition(
                "SELECT id AS Id, code AS Code, name AS Name, assigned_sales_rep AS SalesRep FROM customers WHERE id = @CustomerId;",
                new { request.CustomerId },
                transaction,
                cancellationToken: cancellationToken));
        if (customer.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<InvoiceDto>.Failure(new Error("Customer.NotFound", "Customer was not found."));
        }

        // The rep is the one chosen on the invoice, or the customer's own rep.
        var salesRepId = request.SalesRepId is { } chosen && chosen != Guid.Empty ? chosen : customer.SalesRep;
        if (salesRepId is { } rep && !await SalesRepGuard.IsActiveAsync(connection, transaction, rep, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<InvoiceDto>.Failure(SalesRepGuard.NotActive);
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
                request.FxRateId,
                FxRateSnapshot = fxRate.MidRate,
                SalesRepId = salesRepId,
                CreatedBy = _currentUser.UserId
            },
            transaction,
            cancellationToken: cancellationToken));

        var lineNumber = 1;
        foreach (var line in request.Lines)
        {
            var sku = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Code, string Name, string NameAr, decimal MinSellingPriceSyp, decimal MinSellingPriceUsd, decimal CostSyp, decimal CostUsd)>(
                new CommandDefinition(
                    "SELECT id AS Id, code AS Code, name AS Name, name_ar AS NameAr, min_selling_price_syp AS MinSellingPriceSyp, min_selling_price_usd AS MinSellingPriceUsd, cost_price_syp AS CostSyp, cost_price_usd AS CostUsd FROM skus WHERE id = @SkuId;",
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

            // Cost drives cost of goods sold and margin: use the batch's own cost when the line names a batch, else the SKU cost.
            var cost = (Syp: sku.CostSyp, Usd: sku.CostUsd);
            if (line.BatchId is not null)
            {
                var batchCost = await connection.QuerySingleOrDefaultAsync<(decimal Syp, decimal Usd)?>(new CommandDefinition(
                    "SELECT cost_price_syp AS Syp, cost_price_usd AS Usd FROM batches WHERE id = @BatchId;",
                    new { BatchId = line.BatchId }, transaction, cancellationToken: cancellationToken));
                if (batchCost is { } bc && (bc.Syp > 0 || bc.Usd > 0))
                {
                    cost = (bc.Syp, bc.Usd);
                }
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO invoice_lines (
                    id, invoice_id, line_number, sku_id, batch_id, location_id, description, quantity,
                    unit_price_syp, unit_price_usd, discount_pct, cost_price_syp, cost_price_usd,
                    fx_rate_used, is_price_override, price_override_reason, created_at)
                VALUES (
                    @Id, @InvoiceId, @LineNumber, @SkuId, @BatchId, @LocationId, @Description, @Quantity,
                    @UnitPriceSyp, @UnitPriceUsd, @DiscountPct, @CostSyp, @CostUsd, @FxRateUsed, @IsPriceOverride, @OverrideReason, now());
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
                    CostSyp = cost.Syp,
                    CostUsd = cost.Usd,
                    FxRateUsed = fxRate.MidRate,
                    line.IsPriceOverride,
                    OverrideReason = line.OverrideReason
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        // Lines were inserted directly, so roll the header totals up now (a draft used to be saved with total 0).
        var discount = await InvoiceTotals.ApplyDiscountAsync(connection, transaction, invoiceId, request.DiscountPct, request.DiscountAmountUsd, cancellationToken);
        if (discount.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<InvoiceDto>.Failure(discount.Error);
        }

        var invoice = await InvoiceReader.LoadAsync(connection, transaction, invoiceId, cancellationToken);
        if (invoice is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<InvoiceDto>.Failure(new Error("Invoice.NotFound", "Invoice was not found after creation."));
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<InvoiceDto>.Success(invoice);
    }
}

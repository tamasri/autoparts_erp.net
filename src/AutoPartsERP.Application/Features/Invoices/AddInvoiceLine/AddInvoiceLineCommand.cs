using Dapper;

namespace AutoPartsERP.Application.Features.Invoices.AddInvoiceLine;

public sealed record AddInvoiceLineCommand(
    Guid InvoiceId,
    CreateInvoiceLineRequest Line,
    string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.Update;
    public string AuditModule => "INVOICES";
}

public sealed class AddInvoiceLineCommandValidator : AbstractValidator<AddInvoiceLineCommand>
{
    public AddInvoiceLineCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Line).NotNull();
        RuleFor(x => x.Line.SkuId).NotEmpty();
        RuleFor(x => x.Line.LocationId).NotEmpty();
        RuleFor(x => x.Line.Quantity).GreaterThan(0);
        RuleFor(x => x.Line.UnitPriceSyp).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Line.UnitPriceUsd).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Line.DiscountPct).InclusiveBetween(0, 100);
    }
}

public sealed class AddInvoiceLineCommandHandler : IRequestHandler<AddInvoiceLineCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public AddInvoiceLineCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(AddInvoiceLineCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var invoiceStatus = await connection.ExecuteScalarAsync<string?>(
            new CommandDefinition(
                "SELECT status FROM invoices WHERE id = @InvoiceId;",
                new { request.InvoiceId },
                transaction,
                cancellationToken: cancellationToken));

        if (!string.Equals(invoiceStatus, "DRAFT", StringComparison.OrdinalIgnoreCase))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Invoice.InvalidState", "Only draft invoices can be modified."));
        }

        var sku = await connection.QuerySingleOrDefaultAsync<(string Name, decimal CostPriceSyp, decimal CostPriceUsd)>(
            new CommandDefinition(
                "SELECT name AS Name, cost_price_syp AS CostPriceSyp, cost_price_usd AS CostPriceUsd FROM skus WHERE id = @SkuId;",
                new { request.Line.SkuId },
                transaction,
                cancellationToken: cancellationToken));

        if (string.IsNullOrWhiteSpace(sku.Name))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Sku.NotFound", "SKU was not found."));
        }

        var nextLineNumber = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COALESCE(MAX(line_number), 0) + 1 FROM invoice_lines WHERE invoice_id = @InvoiceId;",
                new { request.InvoiceId },
                transaction,
                cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO invoice_lines (
                id, invoice_id, line_number, sku_id, batch_id, location_id, description, quantity,
                unit_price_syp, unit_price_usd, discount_pct, cost_price_syp, cost_price_usd,
                fx_rate_used, is_price_override, price_override_reason, created_at)
            VALUES (
                @Id, @InvoiceId, @LineNumber, @SkuId, @BatchId, @LocationId, @Description, @Quantity,
                @UnitPriceSyp, @UnitPriceUsd, @DiscountPct, @CostPriceSyp, @CostPriceUsd,
                0, @IsPriceOverride, @OverrideReason, now());
            """,
            new
            {
                Id = Guid.NewGuid(),
                request.InvoiceId,
                LineNumber = nextLineNumber,
                request.Line.SkuId,
                request.Line.BatchId,
                request.Line.LocationId,
                Description = sku.Name,
                request.Line.Quantity,
                request.Line.UnitPriceSyp,
                request.Line.UnitPriceUsd,
                request.Line.DiscountPct,
                CostPriceSyp = sku.CostPriceSyp,
                CostPriceUsd = sku.CostPriceUsd,
                request.Line.IsPriceOverride,
                OverrideReason = request.Line.OverrideReason
            },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE invoices
            SET subtotal_syp = COALESCE((SELECT SUM(line_total_syp) FROM invoice_lines WHERE invoice_id = @InvoiceId), 0),
                subtotal_usd = COALESCE((SELECT SUM(line_total_usd) FROM invoice_lines WHERE invoice_id = @InvoiceId), 0),
                total_syp = COALESCE((SELECT SUM(line_total_syp) FROM invoice_lines WHERE invoice_id = @InvoiceId), 0) - discount_amount_syp + delivery_fee_syp + tax_amount_syp,
                total_usd = COALESCE((SELECT SUM(line_total_usd) FROM invoice_lines WHERE invoice_id = @InvoiceId), 0) - discount_amount_usd + delivery_fee_usd + tax_amount_usd,
                updated_at = now(),
                updated_by = @UpdatedBy
            WHERE id = @InvoiceId;
            """,
            new { request.InvoiceId, UpdatedBy = _currentUser.UserId },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(request.InvoiceId);
    }
}

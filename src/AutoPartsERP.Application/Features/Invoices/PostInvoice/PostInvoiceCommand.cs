using AutoPartsERP.Application.Features.Inventory;
using AutoPartsERP.Application.Features.Invoices.Returns;
using Dapper;
using Humanizer;

namespace AutoPartsERP.Application.Features.Invoices.PostInvoice;

// The period lock is checked in the handler against the invoice's own date (InvoicePeriod), not the day of posting.
public sealed record PostInvoiceCommand(
    Guid InvoiceId,
    string IdempotencyKey,
    string? ModuleInput = null)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.Post;
    public string AuditModule => "INVOICES";

    // Posting decrements live stock, creates warranty records, and fires an outbox event — requires a second approver.
    public bool RequiresApproval => true;
}

public sealed class PostInvoiceCommandValidator : AbstractValidator<PostInvoiceCommand>
{
    public PostInvoiceCommandValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty();
    }
}

public sealed class PostInvoiceCommandHandler : IRequestHandler<PostInvoiceCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IPeriodLockService _periodLock;

    public PostInvoiceCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, IPeriodLockService periodLock)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _periodLock = periodLock;
    }

    public async Task<Result<Guid>> Handle(PostInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var header = await connection.QuerySingleOrDefaultAsync<PostHeaderRow>(
            new CommandDefinition(
                """
                SELECT
                    i.id AS Id,
                    i.status AS Status,
                    i.customer_id AS CustomerId,
                    c.code AS CustomerCode,
                    c.name AS CustomerName,
                    i.invoice_date AS InvoiceDate,
                    i.due_date AS DueDate,
                    i.fx_rate_id AS FxRateId,
                    i.fx_rate_snapshot AS FxRateSnapshot,
                    i.total_syp AS TotalSyp,
                    i.total_usd AS TotalUsd,
                    i.paid_syp AS PaidSyp,
                    i.paid_usd AS PaidUsd,
                    i.sales_rep_id AS SalesRepId,
                    i.invoice_type AS Type,
                    i.original_invoice_id AS OriginalInvoiceId
                FROM invoices i
                INNER JOIN customers c ON c.id = i.customer_id
                WHERE i.id = @InvoiceId
                FOR UPDATE;
                """,
                new { request.InvoiceId },
                transaction,
                cancellationToken: cancellationToken));

        if (header is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Invoice.NotFound", "Invoice was not found."));
        }

        if (!string.Equals(header.Status, "CONFIRMED", StringComparison.OrdinalIgnoreCase))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Invoice.InvalidState", "Only confirmed invoices can be posted."));
        }

        var open = await InvoicePeriod.EnsureOpenAsync(_periodLock, connection, transaction, request.InvoiceId, cancellationToken);
        if (open.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(open.Error);
        }

        var lines = (await connection.QueryAsync<PostLineRow>(
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
                    il.price_override_reason AS OverrideReason,
                    il.cost_price_usd AS CostPriceUsd,
                    s.has_warranty AS HasWarranty,
                    s.warranty_months AS WarrantyMonths
                FROM invoice_lines il
                INNER JOIN skus s ON s.id = il.sku_id
                WHERE il.invoice_id = @InvoiceId
                ORDER BY il.line_number
                FOR UPDATE;
                """,
                new { request.InvoiceId },
                transaction,
                cancellationToken: cancellationToken))).ToArray();

        if (lines.Length == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Invoice.NoLines", "Cannot post an invoice with no lines."));
        }

        var isReturn = string.Equals(header.Type, "RETURN", StringComparison.OrdinalIgnoreCase);
        (Guid Id, decimal BalanceSyp, decimal BalanceUsd)? sale = null;
        if (isReturn)
        {
            // Lock the returned sale first: two returns of one sale are posted one after the other, never side by side.
            sale = await connection.QuerySingleOrDefaultAsync<(Guid Id, decimal BalanceSyp, decimal BalanceUsd)?>(new CommandDefinition(
                "SELECT id AS Id, balance_syp AS BalanceSyp, balance_usd AS BalanceUsd FROM invoices WHERE id = @OriginalInvoiceId AND status = 'POSTED' FOR UPDATE;",
                new { header.OriginalInvoiceId }, transaction, cancellationToken: cancellationToken));
            if (sale is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("SalesReturn.OriginalNotPosted", "The invoice being returned is no longer posted."));
            }

            var over = await SalesReturnLines.OverReturnedAsync(connection, transaction, request.InvoiceId, cancellationToken);
            if (over is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("SalesReturn.ExceedsSold", $"Item {over} would be returned beyond what the invoice still holds."));
            }
        }

        foreach (var line in lines)
        {
            if (isReturn)
            {
                // The goods come back at the cost they were sold at (ERPNext: the return's incoming rate is the sale's valuation rate).
                await InventoryCosting.BlendInAsync(connection, transaction, line.SkuId, line.Quantity, line.CostPriceUsd, header.FxRateSnapshot, _currentUser.UserId, cancellationToken);
            }

            var moved = await InvoiceStockMover.MoveAsync(
                connection, transaction, request.InvoiceId, line.SkuId, line.LocationId, line.BatchId, line.Quantity,
                isReturn ? StockDirection.In : StockDirection.Out, _currentUser.UserId,
                $"Posted invoice {request.InvoiceId}", cancellationToken);
            if (moved.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(moved.Error);
            }

            if (!isReturn && line.HasWarranty)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO warranty_records (
                        id, warranty_number, invoice_id, invoice_line_id, sku_id, batch_id, customer_id,
                        sale_date, expiry_date, status, created_at, created_by)
                    VALUES (
                        @Id, @WarrantyNumber, @InvoiceId, @InvoiceLineId, @SkuId, @BatchId, @CustomerId,
                        @SaleDate, @ExpiryDate, 'ACTIVE', now(), @CreatedBy);
                    """,
                    new
                    {
                        Id = Guid.NewGuid(),
                        WarrantyNumber = $"WR-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                        InvoiceId = request.InvoiceId,
                        InvoiceLineId = line.Id,
                        SkuId = line.SkuId,
                        BatchId = line.BatchId,
                        CustomerId = header.CustomerId,
                        SaleDate = header.InvoiceDate,
                        ExpiryDate = header.InvoiceDate.AddMonths(line.WarrantyMonths),
                        CreatedBy = _currentUser.UserId
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }
        }

        if (sale is { } returned)
        {
            // The credit lands on the returned sale, up to what is still owed on it; the rest stays on the return as the customer's credit.
            var applied = (
                Syp: Math.Min(-header.TotalSyp, Math.Max(returned.BalanceSyp, 0m)),
                Usd: Math.Min(-header.TotalUsd, Math.Max(returned.BalanceUsd, 0m)));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE invoices SET credit_applied_syp = credit_applied_syp + @Syp, credit_applied_usd = credit_applied_usd + @Usd, updated_at = now() WHERE id = @SaleId;
                UPDATE invoices SET credit_applied_syp = -@Syp, credit_applied_usd = -@Usd WHERE id = @ReturnId;
                """,
                new { applied.Syp, applied.Usd, SaleId = returned.Id, ReturnId = request.InvoiceId }, transaction, cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE invoices
            SET status = 'POSTED',
                posted_at = now(),
                posted_by = @PostedBy,
                updated_at = now(),
                updated_by = @PostedBy
            WHERE id = @InvoiceId;
            """,
            new { request.InvoiceId, PostedBy = _currentUser.UserId },
            transaction,
            cancellationToken: cancellationToken));

        var outboxMessage = OutboxMessage.Create(
            OutboxEventTypes.InvoicePosted,
            "Invoice",
            request.InvoiceId,
            new InvoicePostedPayload(
                request.InvoiceId,
                header.CustomerId,
                header.TotalSyp,
                header.TotalUsd,
                header.InvoiceDate,
                header.SalesRepId,
                lines.Length),
            _currentUser.CorrelationId);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO outbox_messages (
                id, event_type, aggregate_type, aggregate_id, payload_json, occurred_at,
                processed_at, processing_error, retry_count, correlation_id)
            VALUES (
                @Id, @EventType, @AggregateType, @AggregateId, @PayloadJson, @OccurredAt,
                @ProcessedAt, @ProcessingError, @RetryCount, @CorrelationId);
            """,
            new
            {
                outboxMessage.Id,
                outboxMessage.EventType,
                outboxMessage.AggregateType,
                outboxMessage.AggregateId,
                outboxMessage.PayloadJson,
                outboxMessage.OccurredAt,
                outboxMessage.ProcessedAt,
                outboxMessage.ProcessingError,
                outboxMessage.RetryCount,
                outboxMessage.CorrelationId
            },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(request.InvoiceId);
    }

    private sealed record PostHeaderRow(
        Guid Id, string Status, Guid CustomerId, string CustomerCode, string CustomerName, DateOnly InvoiceDate, DateOnly DueDate,
        Guid FxRateId, decimal FxRateSnapshot, decimal TotalSyp, decimal TotalUsd, decimal PaidSyp, decimal PaidUsd, Guid? SalesRepId, string Type,
        Guid? OriginalInvoiceId);

    private sealed record PostLineRow(
        Guid Id, int LineNumber, Guid SkuId, string SkuCode, string SkuName, Guid? BatchId, Guid LocationId, decimal Quantity,
        decimal UnitPriceSyp, decimal UnitPriceUsd, decimal DiscountPct, decimal LineTotalSyp, decimal LineTotalUsd,
        decimal GrossMarginSyp, decimal GrossMarginUsd, decimal GrossMarginPct, bool IsPriceOverride, string? OverrideReason,
        decimal CostPriceUsd, bool HasWarranty, int WarrantyMonths);
}

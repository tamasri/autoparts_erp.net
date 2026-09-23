using AutoPartsERP.Application.Features.Inventory;
using AutoPartsERP.Application.Common.Messaging;
using AutoPartsERP.Application.Common.Pricing;
using Dapper;

namespace AutoPartsERP.Application.Features.Purchasing;

// ---------------------------------------------------------------- create

public sealed record CreatePurchaseInvoiceCommand(CreatePurchaseInvoiceRequest Request, string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest, IPeriodSensitiveRequest
{
    public string RequiredPermission => PermissionCodes.Purchases.Create;
    public string AuditModule => "PURCHASES";
    public DateTimeOffset OperationDate => Request.BillDate.ToDateTime(TimeOnly.MinValue);
    public string Module => "PURCHASES";
}

public sealed class CreatePurchaseInvoiceCommandValidator : AbstractValidator<CreatePurchaseInvoiceCommand>
{
    public CreatePurchaseInvoiceCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Request.SupplierPartyId).NotEmpty();
        RuleFor(x => x.Request.WarehouseId).NotEmpty();
        RuleFor(x => x.Request.DueDate).GreaterThanOrEqualTo(x => x.Request.BillDate);
        RuleFor(x => x.Request.Lines).NotEmpty().WithMessage("A bill needs at least one line.");
        RuleForEach(x => x.Request.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitCostUsd).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.DiscountPct).InclusiveBetween(0, 100);
        });
        RuleFor(x => x.Request).Must(r => r.DiscountPct is null || r.DiscountAmountUsd is null).WithMessage("Give the invoice discount as a percentage or as an amount, not both.");
        RuleFor(x => x.Request.DiscountPct).InclusiveBetween(0, 100).When(x => x.Request.DiscountPct is not null);
        RuleFor(x => x.Request.DiscountAmountUsd).GreaterThanOrEqualTo(0).When(x => x.Request.DiscountAmountUsd is not null);
    }
}

public sealed class CreatePurchaseInvoiceCommandHandler : IRequestHandler<CreatePurchaseInvoiceCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreatePurchaseInvoiceCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(CreatePurchaseInvoiceCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var isVendor = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM party_type_assignments WHERE party_id = @PartyId AND type_code = 'VENDOR' AND is_active);",
            new { PartyId = request.SupplierPartyId }, cancellationToken: cancellationToken));
        if (!isVendor)
        {
            return Result<Guid>.Failure(new Error("Purchase.NotASupplier", "The chosen account is not an active supplier."));
        }

        if (!await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM locations WHERE id = @Id AND is_active);", new { Id = request.WarehouseId }, cancellationToken: cancellationToken)))
        {
            return Result<Guid>.Failure(new Error("Purchase.WarehouseNotFound", "The receiving warehouse does not exist or is inactive."));
        }

        var itemIds = request.Lines.Select(l => l.ItemId).Distinct().ToArray();
        var sellable = (await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT id FROM items WHERE id = ANY(@itemIds) AND is_active AND sku_id IS NOT NULL;", new { itemIds }, cancellationToken: cancellationToken))).ToHashSet();
        var missing = itemIds.FirstOrDefault(i => !sellable.Contains(i));
        if (missing != Guid.Empty)
        {
            return Result<Guid>.Failure(new Error("Purchase.ItemNotFound", "One of the items is inactive or has no catalogue (SKU) record."));
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var id = Guid.NewGuid();
        var lines = request.Lines.Select((l, i) => (Line: l, Number: i + 1, Total: Math.Round(l.Quantity * l.UnitCostUsd * (1 - l.DiscountPct / 100m), 4))).ToList();
        var subtotal = lines.Sum(l => l.Total);
        var discount = DocumentDiscount.Resolve(subtotal, request.DiscountPct, request.DiscountAmountUsd);
        if (discount.IsFailure)
        {
            return Result<Guid>.Failure(discount.Error);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO purchase_invoices (
                id, bill_number, supplier_party_id, supplier_ref, bill_date, due_date, warehouse_id, status, fx_rate_id,
                subtotal_usd, discount_pct, discount_amount_usd, total_usd, paid_usd, balance_usd, notes, created_by)
            VALUES (
                @id, 'PUR-' || to_char(@BillDate, 'YYYY') || '-' || lpad(nextval('purchase_invoice_seq')::text, 5, '0'),
                @SupplierPartyId, @SupplierRef, @BillDate, @DueDate, @WarehouseId, 'DRAFT', @FxRateId, @Subtotal, @DiscountPct, @DiscountAmount, @Total, 0, 0, @Notes, @By);
            """,
            new
            {
                id, request.SupplierPartyId, SupplierRef = request.SupplierRef?.Trim(), request.BillDate, request.DueDate, request.WarehouseId,
                request.FxRateId, Subtotal = subtotal, DiscountPct = discount.Value!.Percent, DiscountAmount = discount.Value.Amount,
                Total = subtotal - discount.Value.Amount, Notes = request.Notes?.Trim(), By = _currentUser.UserId
            },
            transaction, cancellationToken: cancellationToken));

        foreach (var (line, number, total) in lines)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO purchase_invoice_lines (purchase_invoice_id, line_number, item_id, quantity, unit_cost_usd, discount_pct, line_total_usd)
                VALUES (@id, @number, @ItemId, @Quantity, @UnitCostUsd, @DiscountPct, @total);
                """,
                new { id, number, line.ItemId, line.Quantity, line.UnitCostUsd, line.DiscountPct, total }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(id);
    }
}

// ---------------------------------------------------------------- read

public sealed record GetPurchaseInvoicesQuery(int PageNumber, int PageSize, string? Status, Guid? SupplierPartyId, string? Search, bool OpenOnly = false)
    : IRequest<Result<PagedResponse<PurchaseInvoiceListDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Purchases.Read;
}

public sealed class GetPurchaseInvoicesQueryHandler : IRequestHandler<GetPurchaseInvoicesQuery, Result<PagedResponse<PurchaseInvoiceListDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetPurchaseInvoicesQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<PurchaseInvoiceListDto>>> Handle(GetPurchaseInvoicesQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(request.PageNumber, 1);
        var size = Math.Clamp(request.PageSize, 1, 200);
        var args = new
        {
            Status = string.IsNullOrWhiteSpace(request.Status) ? null : request.Status.Trim().ToUpperInvariant(),
            request.SupplierPartyId,
            Search = string.IsNullOrWhiteSpace(request.Search) ? null : $"%{request.Search.Trim()}%",
            request.OpenOnly,
            Offset = (page - 1) * size,
            Size = size
        };
        const string where = """
            WHERE (@Status::text IS NULL OR p.status = @Status)
              AND (@SupplierPartyId::uuid IS NULL OR p.supplier_party_id = @SupplierPartyId)
              AND (@Search::text IS NULL OR p.bill_number ILIKE @Search OR p.supplier_ref ILIKE @Search OR pa.display_name ILIKE @Search OR pa.display_name_ar ILIKE @Search)
              AND (NOT @OpenOnly OR (p.status = 'POSTED' AND p.balance_usd > 0))
            """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<PurchaseInvoiceListDto>(new CommandDefinition(
            $"""
            SELECT p.id AS Id, p.bill_number AS BillNumber, p.supplier_party_id AS SupplierPartyId,
                   COALESCE(NULLIF(pa.display_name_ar, ''), pa.display_name) AS SupplierName, p.supplier_ref AS SupplierRef,
                   p.bill_date AS BillDate, p.due_date AS DueDate, p.status AS Status,
                   p.total_usd AS TotalUsd, p.paid_usd AS PaidUsd, p.balance_usd AS BalanceUsd
            FROM purchase_invoices p
            INNER JOIN parties pa ON pa.id = p.supplier_party_id
            {where}
            ORDER BY p.bill_date DESC, p.created_at DESC
            OFFSET @Offset LIMIT @Size;
            """,
            args, cancellationToken: cancellationToken))).ToArray();
        var total = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            $"SELECT count(*) FROM purchase_invoices p INNER JOIN parties pa ON pa.id = p.supplier_party_id {where};", args, cancellationToken: cancellationToken));
        return Result<PagedResponse<PurchaseInvoiceListDto>>.Success(new PagedResponse<PurchaseInvoiceListDto>(rows, page, size, total));
    }
}

public sealed record GetPurchaseInvoiceByIdQuery(Guid Id)
    : IRequest<Result<PurchaseInvoiceDetailDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Purchases.Read;
}

public sealed class GetPurchaseInvoiceByIdQueryHandler : IRequestHandler<GetPurchaseInvoiceByIdQuery, Result<PurchaseInvoiceDetailDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetPurchaseInvoiceByIdQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private sealed record Header(
        Guid Id, string BillNumber, Guid SupplierPartyId, string SupplierName, string? SupplierRef, DateOnly BillDate, DateOnly DueDate, string Status,
        decimal TotalUsd, decimal PaidUsd, decimal BalanceUsd, Guid WarehouseId, string? Notes, string? VoidReason, DateTimeOffset? PostedAt,
        decimal SubtotalUsd, decimal? DiscountPct, decimal DiscountAmountUsd);

    public async Task<Result<PurchaseInvoiceDetailDto>> Handle(GetPurchaseInvoiceByIdQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var h = await connection.QuerySingleOrDefaultAsync<Header>(new CommandDefinition(
            """
            SELECT p.id AS Id, p.bill_number AS BillNumber, p.supplier_party_id AS SupplierPartyId,
                   COALESCE(NULLIF(pa.display_name_ar, ''), pa.display_name) AS SupplierName, p.supplier_ref AS SupplierRef,
                   p.bill_date AS BillDate, p.due_date AS DueDate, p.status AS Status, p.total_usd AS TotalUsd, p.paid_usd AS PaidUsd,
                   p.balance_usd AS BalanceUsd, p.warehouse_id AS WarehouseId, p.notes AS Notes, p.void_reason AS VoidReason, p.posted_at AS PostedAt,
                   p.subtotal_usd AS SubtotalUsd, p.discount_pct AS DiscountPct, p.discount_amount_usd AS DiscountAmountUsd
            FROM purchase_invoices p INNER JOIN parties pa ON pa.id = p.supplier_party_id
            WHERE p.id = @Id;
            """,
            new { request.Id }, cancellationToken: cancellationToken));
        if (h is null)
        {
            return Result<PurchaseInvoiceDetailDto>.Failure(new Error("Purchase.NotFound", "Purchase invoice was not found."));
        }

        var lines = (await connection.QueryAsync<PurchaseLineDto>(new CommandDefinition(
            """
            SELECT l.id AS Id, l.line_number AS LineNumber, l.item_id AS ItemId, i.part_number AS ItemCode,
                   COALESCE(NULLIF(i.name_ar, ''), i.name_en) AS ItemName, l.quantity AS Quantity, l.unit_cost_usd AS UnitCostUsd,
                   l.discount_pct AS DiscountPct, l.line_total_usd AS LineTotalUsd
            FROM purchase_invoice_lines l INNER JOIN items i ON i.id = l.item_id
            WHERE l.purchase_invoice_id = @Id ORDER BY l.line_number;
            """,
            new { request.Id }, cancellationToken: cancellationToken))).ToArray();

        var list = new PurchaseInvoiceListDto(h.Id, h.BillNumber, h.SupplierPartyId, h.SupplierName, h.SupplierRef, h.BillDate, h.DueDate, h.Status, h.TotalUsd, h.PaidUsd, h.BalanceUsd);
        return Result<PurchaseInvoiceDetailDto>.Success(new PurchaseInvoiceDetailDto(list, h.WarehouseId, h.Notes, h.VoidReason, h.PostedAt, lines, h.SubtotalUsd, h.DiscountPct, h.DiscountAmountUsd));
    }
}

// ---------------------------------------------------------------- post

/// <summary>
/// Posting a bill is what receives the goods: stock goes into the warehouse (sellable at once), the item ledger records it, the
/// item cost becomes the weighted average of what was on hand and what was bought, and the bill is handed to ERPNext.
/// </summary>
public sealed record PostPurchaseInvoiceCommand(Guid Id)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Purchases.Post;
    public string AuditModule => "PURCHASES";
}

public sealed class PostPurchaseInvoiceCommandHandler : IRequestHandler<PostPurchaseInvoiceCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IPeriodLockService _periodLock;

    public PostPurchaseInvoiceCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, IPeriodLockService periodLock)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _periodLock = periodLock;
    }

    public async Task<Result<Guid>> Handle(PostPurchaseInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var bill = await connection.QuerySingleOrDefaultAsync<(Guid Id, string BillNumber, string Status, Guid WarehouseId, DateOnly BillDate, decimal Total, Guid? FxRateId)>(new CommandDefinition(
            """
            SELECT id AS Id, bill_number AS BillNumber, status AS Status, warehouse_id AS WarehouseId, bill_date AS BillDate,
                   total_usd AS Total, fx_rate_id AS FxRateId
            FROM purchase_invoices WHERE id = @Id FOR UPDATE;
            """,
            new { request.Id }, transaction, cancellationToken: cancellationToken));
        if (bill.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Purchase.NotFound", "Purchase invoice was not found."));
        }

        if (bill.Status != "DRAFT")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Purchase.InvalidState", "Only a draft bill can be posted."));
        }

        if (await _periodLock.IsLockedAsync(bill.BillDate.Year, bill.BillDate.Month, "PURCHASES", cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Period.Locked", "The accounting period of this bill is locked."));
        }

        var fx = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            "SELECT COALESCE((SELECT mid_rate FROM fx_rates WHERE id = @FxRateId), (SELECT mid_rate FROM fx_rates WHERE is_active ORDER BY rate_date DESC, created_at DESC LIMIT 1));",
            new { bill.FxRateId }, transaction, cancellationToken: cancellationToken)) ?? 0m;

        var lines = (await connection.QueryAsync<(Guid ItemId, Guid SkuId, decimal Qty, decimal NetCost)>(new CommandDefinition(
            """
            SELECT l.item_id AS ItemId, i.sku_id AS SkuId, l.quantity AS Qty,
                   round(l.unit_cost_usd * (1 - l.discount_pct / 100) * CASE WHEN p.subtotal_usd = 0 THEN 1 ELSE p.total_usd / p.subtotal_usd END, 6) AS NetCost
            FROM purchase_invoice_lines l
            INNER JOIN items i ON i.id = l.item_id
            INNER JOIN purchase_invoices p ON p.id = l.purchase_invoice_id
            WHERE l.purchase_invoice_id = @Id;
            """,
            new { request.Id }, transaction, cancellationToken: cancellationToken))).ToList();

        foreach (var line in lines)
        {
            // Weighted-average cost over everything on hand in every warehouse, taken BEFORE the new goods are added.
            var sku = await connection.QuerySingleAsync<(decimal OnHand, decimal Cost)>(new CommandDefinition(
                """
                SELECT COALESCE((SELECT SUM(quantity_on_hand) FROM inventory_stock WHERE sku_id = @SkuId), 0) AS OnHand,
                       (SELECT cost_price_usd FROM skus WHERE id = @SkuId FOR UPDATE) AS Cost;
                """,
                new { line.SkuId }, transaction, cancellationToken: cancellationToken));
            var newCost = Math.Round((sku.OnHand * sku.Cost + line.Qty * line.NetCost) / (sku.OnHand + line.Qty), 4);
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE skus SET cost_price_usd = @newCost, cost_price_syp = @newCostSyp, updated_at = now(), updated_by = @By WHERE id = @SkuId;",
                new { line.SkuId, newCost, newCostSyp = Math.Round(newCost * fx, 4), By = _currentUser.UserId }, transaction, cancellationToken: cancellationToken));

            var stocked = await StockLevelWriter.ApplyAvailableAsync(connection, transaction, line.ItemId, bill.WarehouseId, line.Qty, cancellationToken);
            if (stocked.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(stocked.Error);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO inventory_balances (id, item_id, location_id, batch_id, status, qty, updated_at)
                VALUES (uuid_generate_v4(), @ItemId, @WarehouseId, NULL, 'AVAILABLE', @Qty, now())
                ON CONFLICT (item_id, location_id, batch_id, status) DO UPDATE SET qty = inventory_balances.qty + EXCLUDED.qty, updated_at = now();
                """,
                new { line.ItemId, bill.WarehouseId, line.Qty }, transaction, cancellationToken: cancellationToken));

            await InventoryMovementWriter.RecordAsync(
                connection, transaction, line.SkuId, bill.WarehouseId, null, line.Qty, true, "PURCHASE", "PURCHASE_INVOICE", bill.Id,
                _currentUser.UserId, $"Purchase invoice {bill.BillNumber}", cancellationToken);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE purchase_invoices
            SET status = 'POSTED', balance_usd = total_usd, posted_at = now(), posted_by = @By, updated_at = now()
            WHERE id = @Id;
            """,
            new { request.Id, By = _currentUser.UserId }, transaction, cancellationToken: cancellationToken));

        await OutboxWriter.AddAsync(connection, transaction, OutboxEventTypes.PurchaseInvoicePosted, "PurchaseInvoice", request.Id,
            new PurchaseInvoiceEventPayload(request.Id), _currentUser.CorrelationId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(request.Id);
    }
}

// ---------------------------------------------------------------- void

public sealed record VoidPurchaseInvoiceCommand(Guid Id, string Reason)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Purchases.Void;
    public string AuditModule => "PURCHASES";
}

public sealed class VoidPurchaseInvoiceCommandValidator : AbstractValidator<VoidPurchaseInvoiceCommand>
{
    public VoidPurchaseInvoiceCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(5);
    }
}

public sealed class VoidPurchaseInvoiceCommandHandler : IRequestHandler<VoidPurchaseInvoiceCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IPeriodLockService _periodLock;

    public VoidPurchaseInvoiceCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, IPeriodLockService periodLock)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _periodLock = periodLock;
    }

    public async Task<Result<Guid>> Handle(VoidPurchaseInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var bill = await connection.QuerySingleOrDefaultAsync<(Guid Id, string BillNumber, string Status, Guid WarehouseId, DateOnly BillDate, decimal Paid)>(new CommandDefinition(
            "SELECT id AS Id, bill_number AS BillNumber, status AS Status, warehouse_id AS WarehouseId, bill_date AS BillDate, paid_usd AS Paid FROM purchase_invoices WHERE id = @Id FOR UPDATE;",
            new { request.Id }, transaction, cancellationToken: cancellationToken));
        if (bill.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Purchase.NotFound", "Purchase invoice was not found."));
        }

        if (bill.Status == "VOID")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Purchase.InvalidState", "The bill is already void."));
        }

        if (bill.Paid > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Purchase.HasPayments", "Reverse the supplier payments allocated to this bill before voiding it."));
        }

        if (await _periodLock.IsLockedAsync(bill.BillDate.Year, bill.BillDate.Month, "PURCHASES", cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Period.Locked", "The accounting period of this bill is locked."));
        }

        if (bill.Status == "POSTED")
        {
            // The goods leave again; if some were already sold or moved, the bill cannot be voided.
            var lines = (await connection.QueryAsync<(Guid ItemId, Guid SkuId, decimal Qty, decimal NetCost)>(new CommandDefinition(
                """
                SELECT l.item_id AS ItemId, i.sku_id AS SkuId, l.quantity AS Qty,
                       round(l.unit_cost_usd * (1 - l.discount_pct / 100) * CASE WHEN p.subtotal_usd = 0 THEN 1 ELSE p.total_usd / p.subtotal_usd END, 6) AS NetCost
                FROM purchase_invoice_lines l
                INNER JOIN items i ON i.id = l.item_id
                INNER JOIN purchase_invoices p ON p.id = l.purchase_invoice_id
                WHERE l.purchase_invoice_id = @Id;
                """,
                new { request.Id }, transaction, cancellationToken: cancellationToken))).ToList();

            foreach (var line in lines)
            {
                // Take this bill's goods out of the weighted-average cost as well, so the cost goes back to what it was without them.
                var sku = await connection.QuerySingleAsync<(decimal OnHand, decimal Cost)>(new CommandDefinition(
                    """
                    SELECT COALESCE((SELECT SUM(quantity_on_hand) FROM inventory_stock WHERE sku_id = @SkuId), 0) AS OnHand,
                           (SELECT cost_price_usd FROM skus WHERE id = @SkuId FOR UPDATE) AS Cost;
                    """,
                    new { line.SkuId }, transaction, cancellationToken: cancellationToken));
                var remaining = sku.OnHand - line.Qty;
                if (remaining > 0)
                {
                    var restored = Math.Max(Math.Round((sku.OnHand * sku.Cost - line.Qty * line.NetCost) / remaining, 4), 0m);
                    await connection.ExecuteAsync(new CommandDefinition(
                        "UPDATE skus SET cost_price_usd = @restored, updated_at = now(), updated_by = @By WHERE id = @SkuId;",
                        new { line.SkuId, restored, By = _currentUser.UserId }, transaction, cancellationToken: cancellationToken));
                }

                var removed = await StockLevelWriter.ApplyAvailableAsync(connection, transaction, line.ItemId, bill.WarehouseId, -line.Qty, cancellationToken);
                if (removed.IsFailure)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<Guid>.Failure(new Error("Purchase.StockAlreadyUsed", "Part of the received goods was already sold or moved, so the bill cannot be voided."));
                }

                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE inventory_balances SET qty = GREATEST(qty - @Qty, 0), updated_at = now() WHERE item_id = @ItemId AND location_id = @WarehouseId AND batch_id IS NULL AND status = 'AVAILABLE';",
                    new { line.ItemId, bill.WarehouseId, line.Qty }, transaction, cancellationToken: cancellationToken));

                await InventoryMovementWriter.RecordAsync(
                    connection, transaction, line.SkuId, bill.WarehouseId, null, line.Qty, false, "PURCHASE_VOID", "PURCHASE_INVOICE", bill.Id,
                    _currentUser.UserId, $"Voided purchase invoice {bill.BillNumber}", cancellationToken);
            }
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE purchase_invoices SET status = 'VOID', balance_usd = 0, void_reason = @Reason, voided_at = now(), voided_by = @By, updated_at = now() WHERE id = @Id;",
            new { request.Id, request.Reason, By = _currentUser.UserId }, transaction, cancellationToken: cancellationToken));

        if (bill.Status == "POSTED")
        {
            await OutboxWriter.AddAsync(connection, transaction, OutboxEventTypes.PurchaseInvoiceVoided, "PurchaseInvoice", request.Id,
                new PurchaseInvoiceEventPayload(request.Id), _currentUser.CorrelationId, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(request.Id);
    }
}

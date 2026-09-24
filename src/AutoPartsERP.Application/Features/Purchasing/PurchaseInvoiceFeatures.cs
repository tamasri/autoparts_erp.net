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
                id, supplier_party_id, supplier_ref, bill_date, due_date, warehouse_id, status, fx_rate_id,
                subtotal_usd, discount_pct, discount_amount_usd, total_usd, paid_usd, notes, created_by)
            VALUES (
                @id, @SupplierPartyId, @SupplierRef, @BillDate, @DueDate, @WarehouseId, 'DRAFT', @FxRateId, @Subtotal, @DiscountPct, @DiscountAmount, @Total, 0, @Notes, @By);
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
                   p.total_usd AS TotalUsd, p.paid_usd AS PaidUsd, p.balance_usd AS BalanceUsd, p.is_return AS IsReturn, p.kind AS Kind
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
        decimal TotalUsd, decimal PaidUsd, decimal BalanceUsd, Guid? WarehouseId, string? Notes, string? VoidReason, DateTimeOffset? PostedAt,
        decimal SubtotalUsd, decimal? DiscountPct, decimal DiscountAmountUsd, bool IsReturn, decimal CreditAppliedUsd, Guid? ReturnAgainstId, string Kind);

    public async Task<Result<PurchaseInvoiceDetailDto>> Handle(GetPurchaseInvoiceByIdQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var h = await connection.QuerySingleOrDefaultAsync<Header>(new CommandDefinition(
            """
            SELECT p.id AS Id, p.bill_number AS BillNumber, p.supplier_party_id AS SupplierPartyId,
                   COALESCE(NULLIF(pa.display_name_ar, ''), pa.display_name) AS SupplierName, p.supplier_ref AS SupplierRef,
                   p.bill_date AS BillDate, p.due_date AS DueDate, p.status AS Status, p.total_usd AS TotalUsd, p.paid_usd AS PaidUsd,
                   p.balance_usd AS BalanceUsd, p.warehouse_id AS WarehouseId, p.notes AS Notes, p.void_reason AS VoidReason, p.posted_at AS PostedAt,
                   p.subtotal_usd AS SubtotalUsd, p.discount_pct AS DiscountPct, p.discount_amount_usd AS DiscountAmountUsd,
                   p.is_return AS IsReturn, p.credit_applied_usd AS CreditAppliedUsd, p.return_against_id AS ReturnAgainstId, p.kind AS Kind
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
            SELECT l.id AS Id, l.line_number AS LineNumber, l.item_id AS ItemId, COALESCE(i.part_number, l.charge_type) AS ItemCode,
                   COALESCE(NULLIF(i.name_ar, ''), i.name_en, l.description, l.charge_type) AS ItemName, l.quantity AS Quantity, l.unit_cost_usd AS UnitCostUsd,
                   l.discount_pct AS DiscountPct, l.line_total_usd AS LineTotalUsd, l.return_of_line_id AS ReturnOfLineId, l.charge_type AS ChargeType
            FROM purchase_invoice_lines l LEFT JOIN items i ON i.id = l.item_id
            WHERE l.purchase_invoice_id = @Id ORDER BY l.line_number;
            """,
            new { request.Id }, cancellationToken: cancellationToken))).ToArray();

        // The bill a return gives back to, or the returns made against a bill.
        var links = (await connection.QueryAsync<DocumentLinkDto>(new CommandDefinition(
            """
            SELECT id AS Id, bill_number AS Number, status AS Status, bill_date AS Date, total_usd AS TotalUsd
            FROM purchase_invoices WHERE id = @ReturnAgainstId OR return_against_id = @Id
            ORDER BY serial_no;
            """,
            new { request.Id, h.ReturnAgainstId }, cancellationToken: cancellationToken))).ToList();

        // The landed cost vouchers carrying costs on a goods bill, or the voucher that raised a service bill.
        var vouchers = (await connection.QueryAsync<DocumentLinkDto>(new CommandDefinition(
            """
            SELECT v.id AS Id, v.voucher_number AS Number, v.status AS Status, v.voucher_date AS Date, v.total_usd AS TotalUsd
            FROM landed_cost_vouchers v
            WHERE v.id IN (SELECT voucher_id FROM landed_cost_voucher_bills WHERE purchase_invoice_id = @Id)
               OR v.id = (SELECT landed_cost_voucher_id FROM purchase_invoices WHERE id = @Id)
            ORDER BY v.serial_no;
            """,
            new { request.Id }, cancellationToken: cancellationToken))).ToList();

        var list = new PurchaseInvoiceListDto(h.Id, h.BillNumber, h.SupplierPartyId, h.SupplierName, h.SupplierRef, h.BillDate, h.DueDate, h.Status, h.TotalUsd, h.PaidUsd, h.BalanceUsd, h.IsReturn, h.Kind);
        return Result<PurchaseInvoiceDetailDto>.Success(new PurchaseInvoiceDetailDto(
            list, h.WarehouseId, h.Notes, h.VoidReason, h.PostedAt, lines, h.SubtotalUsd, h.DiscountPct, h.DiscountAmountUsd, h.CreditAppliedUsd,
            links.FirstOrDefault(l => l.Id == h.ReturnAgainstId), links.Where(l => l.Id != h.ReturnAgainstId).ToList(), vouchers));
    }
}

// ---------------------------------------------------------------- post

/// <summary>
/// Posting a bill receives the goods: stock goes into the warehouse (sellable at once), the item ledger records it, the item cost
/// becomes the weighted average of what was on hand and what was bought, and the bill is handed to ERPNext.
/// Posting a purchase return sends goods back: they leave the bill's warehouse at the cost they came in with (the average goes back
/// to what it was without them), and the return's credit is applied to the bill it returns (what exceeds the bill's outstanding stays
/// as the supplier's debit to us). The database refuses a return that would give back more than was bought.
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

        var bill = await PurchaseDocuments.LockAsync(connection, transaction, request.Id, cancellationToken);
        if (bill is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(PurchaseDocuments.NotFound);
        }

        if (bill.Status != "DRAFT" || bill.Kind != "GOODS")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Purchase.InvalidState", "Only a draft goods bill can be posted (a service bill is posted by its landed cost voucher)."));
        }

        if (await _periodLock.IsLockedAsync(bill.BillDate.Year, bill.BillDate.Month, PurchaseDocuments.Module, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(PurchaseDocuments.PeriodLocked);
        }

        PurchaseDocuments.Header? original = null;
        if (bill.ReturnAgainstId is { } againstId)
        {
            // Lock the returned bill first: two returns of one bill are posted one after the other, never side by side.
            original = await PurchaseDocuments.LockAsync(connection, transaction, againstId, cancellationToken);
            if (original is not { Status: "POSTED" })
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("PurchaseReturn.OriginalNotPosted", "The bill being returned is no longer posted."));
            }

            var over = await PurchaseDocuments.OverReturnedAsync(connection, transaction, bill.Id, cancellationToken);
            if (over is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("PurchaseReturn.ExceedsBought", $"Item {over} would be returned beyond what the bill still holds."));
            }
        }

        var fx = await PurchaseDocuments.FxRateAsync(connection, transaction, bill.FxRateId, cancellationToken);
        var lines = await PurchaseDocuments.NetLinesAsync(connection, transaction, bill.Id, cancellationToken);
        foreach (var line in lines)
        {
            Result moved;
            if (bill.IsReturn)
            {
                await InventoryCosting.BlendOutAsync(connection, transaction, line.SkuId, line.Qty, line.NetCost, fx, _currentUser.UserId, cancellationToken);
                moved = await PurchaseDocuments.MoveAsync(connection, transaction, bill, line, isIn: false, "PURCHASE_RETURN", $"Purchase return {bill.BillNumber}", _currentUser.UserId, cancellationToken);
            }
            else
            {
                await InventoryCosting.BlendInAsync(connection, transaction, line.SkuId, line.Qty, line.NetCost, fx, _currentUser.UserId, cancellationToken);
                moved = await PurchaseDocuments.MoveAsync(connection, transaction, bill, line, isIn: true, "PURCHASE", $"Purchase invoice {bill.BillNumber}", _currentUser.UserId, cancellationToken);
            }

            if (moved.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(bill.IsReturn
                    ? new Error("PurchaseReturn.NotInStock", $"The warehouse no longer holds enough of item {line.ItemCode} to send back.")
                    : moved.Error);
            }
        }

        if (original is not null)
        {
            // The credit lands on the returned bill, up to what is still owed on it (ERPNext: return_against lowers its outstanding).
            var applied = Math.Min(-bill.TotalUsd, Math.Max(original.BalanceUsd, 0m));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE purchase_invoices SET credit_applied_usd = credit_applied_usd + @applied, updated_at = now() WHERE id = @OriginalId;
                UPDATE purchase_invoices SET credit_applied_usd = -@applied WHERE id = @ReturnId;
                """,
                new { applied, OriginalId = original.Id, ReturnId = bill.Id }, transaction, cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE purchase_invoices SET status = 'POSTED', posted_at = now(), posted_by = @By, updated_at = now() WHERE id = @Id;",
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

/// <summary>
/// Voiding undoes a posting exactly: a bill's goods leave again (refused once part of them was sold or moved) and the average cost
/// goes back; a return's goods come back and its credit is taken off the bill it returned. A bill with returns is voided only after
/// them (ERPNext likewise refuses to cancel an invoice that has live returns).
/// </summary>
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

        var bill = await PurchaseDocuments.LockAsync(connection, transaction, request.Id, cancellationToken);
        if (bill is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(PurchaseDocuments.NotFound);
        }

        if (bill.Status == "VOID")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Purchase.InvalidState", "The document is already void."));
        }

        if (bill.LandedCostVoucherId is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Purchase.PartOfLandedCost", "This service bill belongs to a landed cost voucher; void the voucher instead."));
        }

        if (bill.PaidUsd > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Purchase.HasPayments", "Reverse the supplier payments allocated to this bill before voiding it."));
        }

        var vouchers = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT string_agg(v.voucher_number, '، ') FROM landed_cost_voucher_bills b INNER JOIN landed_cost_vouchers v ON v.id = b.voucher_id
            WHERE b.purchase_invoice_id = @Id AND v.status <> 'VOID';
            """,
            new { request.Id }, transaction, cancellationToken: cancellationToken));
        if (vouchers is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Purchase.HasLandedCosts", $"Landed cost vouchers {vouchers} carry costs on this bill; void or delete them first."));
        }

        var returns = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT string_agg(bill_number, '، ') FROM purchase_invoices WHERE return_against_id = @Id AND status <> 'VOID';",
            new { request.Id }, transaction, cancellationToken: cancellationToken));
        if (returns is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Purchase.HasReturns", $"Returns {returns} were made against this bill; void them first."));
        }

        if (await _periodLock.IsLockedAsync(bill.BillDate.Year, bill.BillDate.Month, PurchaseDocuments.Module, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(PurchaseDocuments.PeriodLocked);
        }

        if (bill.Status == "POSTED")
        {
            var fx = await PurchaseDocuments.FxRateAsync(connection, transaction, bill.FxRateId, cancellationToken);
            foreach (var line in await PurchaseDocuments.NetLinesAsync(connection, transaction, bill.Id, cancellationToken))
            {
                Result moved;
                if (bill.IsReturn)
                {
                    await InventoryCosting.BlendInAsync(connection, transaction, line.SkuId, line.Qty, line.NetCost, fx, _currentUser.UserId, cancellationToken);
                    moved = await PurchaseDocuments.MoveAsync(connection, transaction, bill, line, isIn: true, "PURCHASE_RETURN_VOID", $"Voided purchase return {bill.BillNumber}", _currentUser.UserId, cancellationToken);
                }
                else
                {
                    await InventoryCosting.BlendOutAsync(connection, transaction, line.SkuId, line.Qty, line.NetCost, fx, _currentUser.UserId, cancellationToken);
                    moved = await PurchaseDocuments.MoveAsync(connection, transaction, bill, line, isIn: false, "PURCHASE_VOID", $"Voided purchase invoice {bill.BillNumber}", _currentUser.UserId, cancellationToken);
                }

                if (moved.IsFailure)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<Guid>.Failure(new Error("Purchase.StockAlreadyUsed", "Part of the received goods was already sold or moved, so the bill cannot be voided."));
                }
            }

            if (bill.ReturnAgainstId is { } againstId)
            {
                // The returned bill owes again what this return had taken off it.
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE purchase_invoices SET credit_applied_usd = credit_applied_usd + @credit, updated_at = now() WHERE id = @againstId;
                    UPDATE purchase_invoices SET credit_applied_usd = 0 WHERE id = @Id;
                    """,
                    new { credit = bill.CreditAppliedUsd, againstId, bill.Id }, transaction, cancellationToken: cancellationToken));
            }
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE purchase_invoices SET status = 'VOID', void_reason = @Reason, voided_at = now(), voided_by = @By, updated_at = now() WHERE id = @Id;",
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

// ---------------------------------------------------------------- shared

/// <summary>What posting, voiding and returning a purchase document read and write, in one place.</summary>
internal static class PurchaseDocuments
{
    public const string Module = "PURCHASES";
    public static readonly Error NotFound = new("Purchase.NotFound", "Purchase invoice was not found.");
    public static readonly Error PeriodLocked = new("Period.Locked", "The accounting period of this document is locked.");

    public sealed record Header(
        Guid Id, string BillNumber, string Status, bool IsReturn, Guid? ReturnAgainstId, Guid? WarehouseId, DateOnly BillDate, Guid? FxRateId,
        decimal TotalUsd, decimal PaidUsd, decimal CreditAppliedUsd, decimal BalanceUsd, Guid SupplierPartyId, string Kind, Guid? LandedCostVoucherId);

    /// <summary>A line with its cost after the line discount and its share of the document discount.</summary>
    public sealed record NetLine(Guid LineId, Guid ItemId, Guid SkuId, string ItemCode, decimal Qty, decimal NetCost);

    public static Task<Header?> LockAsync(DbConnection connection, DbTransaction transaction, Guid id, CancellationToken ct) =>
        connection.QuerySingleOrDefaultAsync<Header>(new CommandDefinition(
            """
            SELECT id AS Id, bill_number AS BillNumber, status AS Status, is_return AS IsReturn, return_against_id AS ReturnAgainstId,
                   warehouse_id AS WarehouseId, bill_date AS BillDate, fx_rate_id AS FxRateId, total_usd AS TotalUsd, paid_usd AS PaidUsd,
                   credit_applied_usd AS CreditAppliedUsd, balance_usd AS BalanceUsd, supplier_party_id AS SupplierPartyId, kind AS Kind,
                   landed_cost_voucher_id AS LandedCostVoucherId
            FROM purchase_invoices WHERE id = @id FOR UPDATE;
            """,
            new { id }, transaction, cancellationToken: ct));

    public static async Task<decimal> FxRateAsync(DbConnection connection, DbTransaction transaction, Guid? fxRateId, CancellationToken ct) =>
        await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            "SELECT COALESCE((SELECT mid_rate FROM fx_rates WHERE id = @fxRateId), (SELECT mid_rate FROM fx_rates WHERE is_active ORDER BY rate_date DESC, created_at DESC LIMIT 1));",
            new { fxRateId }, transaction, cancellationToken: ct)) ?? 0m;

    /// <summary>
    /// The document discount is shared over the lines in proportion to their value (total / subtotal), for a return as for a bill
    /// (both totals are negative on a return, so the share is the same).
    /// </summary>
    public static async Task<IReadOnlyList<NetLine>> NetLinesAsync(DbConnection connection, DbTransaction transaction, Guid id, CancellationToken ct) =>
        (await connection.QueryAsync<NetLine>(new CommandDefinition(
            """
            SELECT l.id AS LineId, l.item_id AS ItemId, i.sku_id AS SkuId, i.part_number AS ItemCode, l.quantity AS Qty,
                   round(l.unit_cost_usd * (1 - l.discount_pct / 100) * CASE WHEN p.subtotal_usd = 0 THEN 1 ELSE p.total_usd / p.subtotal_usd END, 6) AS NetCost
            FROM purchase_invoice_lines l
            INNER JOIN items i ON i.id = l.item_id
            INNER JOIN purchase_invoices p ON p.id = l.purchase_invoice_id
            WHERE l.purchase_invoice_id = @id
            ORDER BY l.line_number;
            """,
            new { id }, transaction, cancellationToken: ct))).ToList();

    /// <summary>The first item this return would take beyond what its bill still holds (every posted return counted), or null.</summary>
    public static Task<string?> OverReturnedAsync(DbConnection connection, DbTransaction transaction, Guid returnId, CancellationToken ct) =>
        connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT i.part_number
            FROM purchase_invoice_lines r
            INNER JOIN purchase_invoice_lines o ON o.id = r.return_of_line_id
            INNER JOIN items i ON i.id = o.item_id
            WHERE r.purchase_invoice_id = @returnId
              AND r.quantity > o.quantity - (SELECT COALESCE(SUM(x.quantity), 0) FROM purchase_invoice_lines x
                                              INNER JOIN purchase_invoices xp ON xp.id = x.purchase_invoice_id
                                              WHERE x.return_of_line_id = o.id AND xp.status = 'POSTED')
            LIMIT 1;
            """,
            new { returnId }, transaction, cancellationToken: ct));

    /// <summary>Moves one line's goods in or out of the document's warehouse (sellable stock, warehouse view and item ledger together).</summary>
    public static async Task<Result> MoveAsync(
        DbConnection connection, DbTransaction transaction, Header bill, NetLine line, bool isIn, string movementType, string note, Guid by, CancellationToken ct)
    {
        var warehouseId = bill.WarehouseId!.Value;
        var stocked = await StockLevelWriter.ApplyAvailableAsync(connection, transaction, line.ItemId, warehouseId, isIn ? line.Qty : -line.Qty, ct);
        if (stocked.IsFailure)
        {
            return stocked;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            isIn
                ? """
                  INSERT INTO inventory_balances (id, item_id, location_id, batch_id, status, qty, updated_at)
                  VALUES (uuid_generate_v4(), @ItemId, @WarehouseId, NULL, 'AVAILABLE', @Qty, now())
                  ON CONFLICT (item_id, location_id, batch_id, status) DO UPDATE SET qty = inventory_balances.qty + EXCLUDED.qty, updated_at = now();
                  """
                : "UPDATE inventory_balances SET qty = GREATEST(qty - @Qty, 0), updated_at = now() WHERE item_id = @ItemId AND location_id = @WarehouseId AND batch_id IS NULL AND status = 'AVAILABLE';",
            new { line.ItemId, WarehouseId = warehouseId, line.Qty }, transaction, cancellationToken: ct));

        await InventoryMovementWriter.RecordAsync(
            connection, transaction, line.SkuId, warehouseId, null, line.Qty, isIn, movementType, "PURCHASE_INVOICE", bill.Id, by, note, ct);
        return Result.Success();
    }
}

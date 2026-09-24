using AutoPartsERP.Application.Common.Messaging;
using AutoPartsERP.Application.Features.Inventory;
using Dapper;

namespace AutoPartsERP.Application.Features.Purchasing.LandedCost;

// Landed cost vouchers (قيد رسملة مصاريف الشراء), after ERPNext's Landed Cost Voucher: the direct costs that follow a purchase —
// transport, customs, shipping, insurance — are gathered on a voucher, split over the goods of one or more posted bills
// (LandedCostCalculator) and added to their cost. A draft shows what posting would do; posting books it: the average cost of the
// goods still on hand rises, the part of goods already sold goes to cost of goods sold, suppliers owed a charge get a service bill,
// and ERPNext gets the matching entry. Voiding undoes all of it while the goods have not moved on since.

/// <summary>The kinds of charge and the ERPNext service item each is billed under.</summary>
public static class LandedCostCharges
{
    public static readonly IReadOnlyList<string> Types = ["FREIGHT", "CUSTOMS", "SHIPPING", "INSURANCE", "OTHER"];

    public static string ServiceItemCode(string chargeType) => $"LANDED-{chargeType}";
}

// ------------------------------------------------------------------ save (create or edit a draft)

public sealed record SaveLandedCostCommand(Guid? Id, SaveLandedCostRequest Request)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest, IPeriodSensitiveRequest
{
    public string RequiredPermission => PermissionCodes.Purchases.Create;
    public string AuditModule => "PURCHASES";
    public DateTimeOffset OperationDate => Request.VoucherDate.ToDateTime(TimeOnly.MinValue);
    public string Module => PurchaseDocuments.Module;
}

public sealed class SaveLandedCostCommandValidator : AbstractValidator<SaveLandedCostCommand>
{
    public SaveLandedCostCommandValidator()
    {
        RuleFor(x => x.Request.Notes).MaximumLength(500);
        RuleFor(x => x.Request.PurchaseInvoiceIds).NotEmpty().WithMessage("Choose the bill(s) whose goods carry the costs.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Each bill once.");
        RuleFor(x => x.Request.Charges).NotEmpty().WithMessage("Add at least one cost.");
        RuleForEach(x => x.Request.Charges).ChildRules(c =>
        {
            c.RuleFor(x => x.ChargeType).Must(t => LandedCostCharges.Types.Contains(t)).WithMessage("Unknown kind of cost.");
            c.RuleFor(x => x.SplitMethod).Must(m => LandedCostCalculator.SplitMethods.Contains(m)).WithMessage("Split by value, quantity or equally.");
            c.RuleFor(x => x.AmountUsd).GreaterThan(0);
            c.RuleFor(x => x.Description).MaximumLength(200);
            c.RuleFor(x => x).Must(x => (x.SupplierPartyId is not null) != !string.IsNullOrWhiteSpace(x.PaidFromAccount))
                .WithMessage("Each cost is either owed to a supplier or paid from an account.");
        });
    }
}

public sealed class SaveLandedCostCommandHandler : IRequestHandler<SaveLandedCostCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IErpNextClient _erpNext;

    public SaveLandedCostCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, IErpNextClient erpNext)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _erpNext = erpNext;
    }

    public async Task<Result<Guid>> Handle(SaveLandedCostCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var accounts = await CheckAccountsAsync(request.Charges.Select(c => c.PaidFromAccount).OfType<string>().Distinct().ToList(), cancellationToken);
        if (accounts.IsFailure)
        {
            return Result<Guid>.Failure(accounts.Error);
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var billIds = request.PurchaseInvoiceIds.ToArray();
        var bills = (await connection.QueryAsync<(Guid Id, string Number, DateOnly Date)>(new CommandDefinition(
            """
            SELECT id AS Id, bill_number AS Number, bill_date AS Date FROM purchase_invoices
            WHERE id = ANY(@billIds) AND status = 'POSTED' AND kind = 'GOODS' AND NOT is_return;
            """,
            new { billIds }, transaction, cancellationToken: cancellationToken))).ToList();
        if (bills.Count != billIds.Length)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("LandedCost.BillNotEligible", "Landed costs go on posted goods bills (not drafts, returns or service bills)."));
        }

        if (bills.FirstOrDefault(b => b.Date > request.VoucherDate) is { Number: not null } later)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("LandedCost.BeforeBill", $"The voucher cannot be dated before bill {later.Number}."));
        }

        var suppliers = request.Charges.Select(c => c.SupplierPartyId).OfType<Guid>().Distinct().ToArray();
        if (suppliers.Length > 0 && await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT count(DISTINCT party_id) FROM party_type_assignments WHERE party_id = ANY(@suppliers) AND type_code = 'VENDOR' AND is_active;",
                new { suppliers }, transaction, cancellationToken: cancellationToken)) != suppliers.Length)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("LandedCost.NotASupplier", "A cost is owed to an account that is not an active supplier."));
        }

        var id = command.Id ?? Guid.NewGuid();
        var total = request.Charges.Sum(c => c.AmountUsd);
        if (command.Id is null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO landed_cost_vouchers (id, voucher_date, status, fx_rate_id, total_usd, notes, created_by)
                VALUES (@id, @VoucherDate, 'DRAFT', @FxRateId, @total, @Notes, @By);
                """,
                new { id, request.VoucherDate, request.FxRateId, total, Notes = request.Notes?.Trim(), By = _currentUser.UserId },
                transaction, cancellationToken: cancellationToken));
        }
        else
        {
            var status = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT status FROM landed_cost_vouchers WHERE id = @id FOR UPDATE;", new { id }, transaction, cancellationToken: cancellationToken));
            if (status != "DRAFT")
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(status is null ? LandedCostDocuments.NotFound : new Error("LandedCost.NotDraft", "Only a draft voucher can be edited."));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE landed_cost_vouchers SET voucher_date = @VoucherDate, fx_rate_id = @FxRateId, total_usd = @total, notes = @Notes, updated_at = now() WHERE id = @id;
                DELETE FROM landed_cost_voucher_bills WHERE voucher_id = @id;
                DELETE FROM landed_cost_charges WHERE voucher_id = @id;
                """,
                new { id, request.VoucherDate, request.FxRateId, total, Notes = request.Notes?.Trim() }, transaction, cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO landed_cost_voucher_bills (voucher_id, purchase_invoice_id) SELECT @id, unnest(@billIds);",
            new { id, billIds }, transaction, cancellationToken: cancellationToken));
        var n = 0;
        foreach (var charge in request.Charges)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO landed_cost_charges (voucher_id, line_number, charge_type, description, amount_usd, split_method, supplier_party_id, paid_from_account)
                VALUES (@id, @n, @ChargeType, @Description, @AmountUsd, @SplitMethod, @SupplierPartyId, @Account);
                """,
                new
                {
                    id, n = ++n, charge.ChargeType, Description = charge.Description?.Trim(), charge.AmountUsd, charge.SplitMethod, charge.SupplierPartyId,
                    Account = string.IsNullOrWhiteSpace(charge.PaidFromAccount) ? null : charge.PaidFromAccount.Trim()
                },
                transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(id);
    }

    /// <summary>A cost paid at once comes out of a cash or bank ledger account of ERPNext's chart (skipped while ERPNext is off).</summary>
    private async Task<Result> CheckAccountsAsync(IReadOnlyList<string> accounts, CancellationToken cancellationToken)
    {
        if (accounts.Count == 0 || !_erpNext.IsEnabled)
        {
            return Result.Success();
        }

        var chart = await _erpNext.GetChartOfAccountsAsync(cancellationToken);
        if (chart.IsFailure)
        {
            return Result.Failure(chart.Error);
        }

        var byName = chart.Value!.ToDictionary(a => a.Name, StringComparer.Ordinal);
        var wrong = accounts.FirstOrDefault(a => !byName.TryGetValue(a.Trim(), out var found) || found.IsGroup || found.AccountType is not ("Cash" or "Bank"));
        return wrong is null
            ? Result.Success()
            : Result.Failure(new Error("LandedCost.NotCashOrBank", $"'{wrong}' is not a cash or bank account of the chart."));
    }
}

// ------------------------------------------------------------------ post

/// <summary>Posting books the voucher: see the file header. Needs a second person's approval like any posting of value (SYSTEM_ADMIN is exempt).</summary>
public sealed record PostLandedCostCommand(Guid Id) : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.Purchases.Post;
    public string AuditModule => "PURCHASES";
    public bool RequiresApproval => true;
}

public sealed class PostLandedCostCommandHandler : IRequestHandler<PostLandedCostCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IPeriodLockService _periodLock;

    public PostLandedCostCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, IPeriodLockService periodLock)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _periodLock = periodLock;
    }

    public async Task<Result<Guid>> Handle(PostLandedCostCommand command, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var voucher = await LandedCostDocuments.LockAsync(connection, transaction, command.Id, cancellationToken);
        if (voucher is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(LandedCostDocuments.NotFound);
        }

        if (voucher.Status != "DRAFT")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("LandedCost.NotDraft", "Only a draft voucher can be posted."));
        }

        if (await _periodLock.IsLockedAsync(voucher.VoucherDate.Year, voucher.VoucherDate.Month, PurchaseDocuments.Module, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(PurchaseDocuments.PeriodLocked);
        }

        // The bills must still be posted goods bills (a bill voided since the draft was saved takes its goods out of the picture).
        var stale = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT string_agg(p.bill_number, '، ') FROM landed_cost_voucher_bills b INNER JOIN purchase_invoices p ON p.id = b.purchase_invoice_id
            WHERE b.voucher_id = @Id AND p.status <> 'POSTED';
            """,
            new { command.Id }, transaction, cancellationToken: cancellationToken));
        if (stale is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("LandedCost.BillNotPosted", $"Bills {stale} are no longer posted."));
        }

        var fx = await PurchaseDocuments.FxRateAsync(connection, transaction, voucher.FxRateId, cancellationToken);
        var lines = await LandedCostDocuments.LinesAsync(connection, transaction, command.Id, cancellationToken);
        var charges = await LandedCostDocuments.ChargesAsync(connection, transaction, command.Id, cancellationToken);
        var onHand = await LandedCostDocuments.OnHandAsync(connection, transaction, lines.Select(l => l.SkuId), cancellationToken);
        var result = LandedCostCalculator.Calculate(
            lines.Select(l => new LandedLine(l.LineId, l.SkuId, l.Quantity, l.NetUnitCost)).ToList(),
            charges.Select(c => new LandedCharge(c.Id, c.AmountUsd, c.SplitMethod)).ToList(),
            onHand);
        if (result.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(result.Error);
        }

        foreach (var share in result.Value!.Shares)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO landed_cost_allocations (voucher_id, charge_id, purchase_invoice_line_id, amount_usd) VALUES (@VoucherId, @ChargeId, @LineId, @Amount);",
                new { VoucherId = command.Id, share.ChargeId, share.LineId, share.Amount }, transaction, cancellationToken: cancellationToken));
        }

        foreach (var effect in result.Value.Effects)
        {
            var (before, after) = await InventoryCosting.AddValueAsync(connection, transaction, effect.SkuId, effect.Capitalized, fx, _currentUser.UserId, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO landed_cost_item_effects (voucher_id, sku_id, quantity, on_hand, allocated_usd, capitalized_usd, expensed_usd, cost_before_usd, cost_after_usd)
                VALUES (@VoucherId, @SkuId, @Quantity, @OnHand, @Allocated, @Capitalized, @Expensed, @before, @after);
                """,
                new { VoucherId = command.Id, effect.SkuId, effect.Quantity, effect.OnHand, effect.Allocated, effect.Capitalized, effect.Expensed, before, after },
                transaction, cancellationToken: cancellationToken));
        }

        // What is owed to suppliers becomes their service bills, posted with the voucher (one per supplier, a line per charge).
        foreach (var owed in charges.Where(c => c.SupplierPartyId is not null).GroupBy(c => c.SupplierPartyId!.Value))
        {
            var billId = Guid.NewGuid();
            var amount = owed.Sum(c => c.AmountUsd);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO purchase_invoices (
                    id, kind, supplier_party_id, bill_date, due_date, warehouse_id, status, fx_rate_id, subtotal_usd, discount_pct, discount_amount_usd,
                    total_usd, paid_usd, notes, landed_cost_voucher_id, created_by, posted_at, posted_by)
                VALUES (
                    @billId, 'SERVICE', @SupplierId, @VoucherDate, @VoucherDate, NULL, 'POSTED', @FxRateId, @amount, NULL, 0,
                    @amount, 0, @Notes, @VoucherId, @By, now(), @By);
                """,
                new
                {
                    billId, SupplierId = owed.Key, voucher.VoucherDate, voucher.FxRateId, amount, Notes = $"مصاريف شراء — {voucher.VoucherNumber}",
                    VoucherId = command.Id, By = _currentUser.UserId
                },
                transaction, cancellationToken: cancellationToken));
            var n = 0;
            foreach (var charge in owed)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO purchase_invoice_lines (purchase_invoice_id, line_number, item_id, charge_type, description, quantity, unit_cost_usd, discount_pct, line_total_usd)
                    VALUES (@billId, @n, NULL, @ChargeType, @Description, 1, @AmountUsd, 0, @AmountUsd);
                    """,
                    new { billId, n = ++n, charge.ChargeType, charge.Description, charge.AmountUsd }, transaction, cancellationToken: cancellationToken));
            }

            await OutboxWriter.AddAsync(connection, transaction, OutboxEventTypes.PurchaseInvoicePosted, "PurchaseInvoice", billId,
                new PurchaseInvoiceEventPayload(billId), _currentUser.CorrelationId, cancellationToken);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE landed_cost_vouchers SET status = 'POSTED', posted_at = now(), posted_by = @By, updated_at = now() WHERE id = @Id;",
            new { command.Id, By = _currentUser.UserId }, transaction, cancellationToken: cancellationToken));
        await OutboxWriter.AddAsync(connection, transaction, OutboxEventTypes.LandedCostPosted, "LandedCost", command.Id,
            new LandedCostEventPayload(command.Id), _currentUser.CorrelationId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(command.Id);
    }
}

// ------------------------------------------------------------------ void

public sealed record VoidLandedCostCommand(Guid Id, string Reason) : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.Purchases.Void;
    public string AuditModule => "PURCHASES";
    public bool RequiresApproval => true;
}

public sealed class VoidLandedCostCommandValidator : AbstractValidator<VoidLandedCostCommand>
{
    public VoidLandedCostCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(5);
    }
}

/// <summary>
/// Voiding takes the capitalized value back out of the average cost and voids the service bills. It is exact only while the goods
/// have not left since posting (a sale or write-off in between carried part of the value into cost of goods sold), so it is refused
/// then — like voiding a bill whose goods were sold; a correcting voucher is the way forward.
/// </summary>
public sealed class VoidLandedCostCommandHandler : IRequestHandler<VoidLandedCostCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IPeriodLockService _periodLock;

    public VoidLandedCostCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, IPeriodLockService periodLock)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _periodLock = periodLock;
    }

    public async Task<Result<Guid>> Handle(VoidLandedCostCommand command, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var voucher = await LandedCostDocuments.LockAsync(connection, transaction, command.Id, cancellationToken);
        if (voucher is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(LandedCostDocuments.NotFound);
        }

        if (voucher.Status == "VOID")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("LandedCost.InvalidState", "The voucher is already void."));
        }

        if (await _periodLock.IsLockedAsync(voucher.VoucherDate.Year, voucher.VoucherDate.Month, PurchaseDocuments.Module, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(PurchaseDocuments.PeriodLocked);
        }

        if (voucher.Status == "POSTED")
        {
            var paid = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT string_agg(bill_number, '، ') FROM purchase_invoices WHERE landed_cost_voucher_id = @Id AND status = 'POSTED' AND paid_usd > 0;",
                new { command.Id }, transaction, cancellationToken: cancellationToken));
            if (paid is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("LandedCost.ServiceBillPaid", $"Service bills {paid} are already paid; reverse those payments first."));
            }

            var moved = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                """
                SELECT string_agg(DISTINCT s.code, '، ')
                FROM landed_cost_item_effects e
                INNER JOIN skus s ON s.id = e.sku_id
                INNER JOIN items i ON i.sku_id = e.sku_id
                INNER JOIN inventory_movements m ON m.item_id = i.id
                WHERE e.voucher_id = @Id AND e.capitalized_usd > 0 AND m.direction = 'OUT' AND m.movement_type <> 'TRANSFER_OUT' AND m.created_at > @PostedAt;
                """,
                new { command.Id, voucher.PostedAt }, transaction, cancellationToken: cancellationToken));
            if (moved is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("LandedCost.GoodsMoved",
                    $"Items {moved} left stock after the voucher was posted, so its cost can no longer be taken back exactly; post a correcting voucher instead."));
            }

            var fx = await PurchaseDocuments.FxRateAsync(connection, transaction, voucher.FxRateId, cancellationToken);
            var effects = await connection.QueryAsync<(Guid SkuId, decimal Capitalized)>(new CommandDefinition(
                "SELECT sku_id AS SkuId, capitalized_usd AS Capitalized FROM landed_cost_item_effects WHERE voucher_id = @Id;",
                new { command.Id }, transaction, cancellationToken: cancellationToken));
            foreach (var (skuId, capitalized) in effects)
            {
                await InventoryCosting.AddValueAsync(connection, transaction, skuId, -capitalized, fx, _currentUser.UserId, cancellationToken);
            }

            var serviceBills = (await connection.QueryAsync<Guid>(new CommandDefinition(
                """
                UPDATE purchase_invoices SET status = 'VOID', void_reason = @Reason, voided_at = now(), voided_by = @By, updated_at = now()
                WHERE landed_cost_voucher_id = @Id AND status = 'POSTED'
                RETURNING id;
                """,
                new { command.Id, command.Reason, By = _currentUser.UserId }, transaction, cancellationToken: cancellationToken))).ToList();
            foreach (var billId in serviceBills)
            {
                await OutboxWriter.AddAsync(connection, transaction, OutboxEventTypes.PurchaseInvoiceVoided, "PurchaseInvoice", billId,
                    new PurchaseInvoiceEventPayload(billId), _currentUser.CorrelationId, cancellationToken);
            }

            await OutboxWriter.AddAsync(connection, transaction, OutboxEventTypes.LandedCostVoided, "LandedCost", command.Id,
                new LandedCostEventPayload(command.Id), _currentUser.CorrelationId, cancellationToken);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE landed_cost_vouchers SET status = 'VOID', void_reason = @Reason, voided_at = now(), voided_by = @By, updated_at = now() WHERE id = @Id;",
            new { command.Id, command.Reason, By = _currentUser.UserId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(command.Id);
    }
}

// ------------------------------------------------------------------ reads

public sealed record GetLandedCostsQuery(int PageNumber, int PageSize, string? Status, Guid? PurchaseInvoiceId)
    : IRequest<Result<PagedResponse<LandedCostListDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Purchases.Read;
}

public sealed class GetLandedCostsQueryHandler : IRequestHandler<GetLandedCostsQuery, Result<PagedResponse<LandedCostListDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetLandedCostsQueryHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result<PagedResponse<LandedCostListDto>>> Handle(GetLandedCostsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(request.PageNumber, 1);
        var size = Math.Clamp(request.PageSize, 1, 200);
        var args = new
        {
            Status = string.IsNullOrWhiteSpace(request.Status) ? null : request.Status.Trim().ToUpperInvariant(),
            request.PurchaseInvoiceId, Offset = (page - 1) * size, Size = size
        };
        const string where = """
            WHERE (@Status::text IS NULL OR v.status = @Status)
              AND (@PurchaseInvoiceId::uuid IS NULL OR EXISTS (SELECT 1 FROM landed_cost_voucher_bills b WHERE b.voucher_id = v.id AND b.purchase_invoice_id = @PurchaseInvoiceId))
            """;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<LandedCostListDto>(new CommandDefinition(
            $"""
            {LandedCostDocuments.ListSelect}
            {where}
            ORDER BY v.serial_no DESC
            OFFSET @Offset LIMIT @Size;
            """,
            args, cancellationToken: cancellationToken));
        var total = await connection.ExecuteScalarAsync<long>(new CommandDefinition($"SELECT count(*) FROM landed_cost_vouchers v {where};", args, cancellationToken: cancellationToken));
        return Result<PagedResponse<LandedCostListDto>>.Success(new PagedResponse<LandedCostListDto>(rows.ToList(), page, size, total));
    }
}

public sealed record GetLandedCostQuery(Guid Id) : IRequest<Result<LandedCostDetailDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Purchases.Read;
}

/// <summary>A posted or voided voucher shows what was booked; a draft shows what posting it now would do.</summary>
public sealed class GetLandedCostQueryHandler : IRequestHandler<GetLandedCostQuery, Result<LandedCostDetailDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetLandedCostQueryHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    private sealed record ChargeRow(
        Guid Id, int LineNumber, string ChargeType, string? Description, decimal AmountUsd, string SplitMethod,
        Guid? SupplierPartyId, string? SupplierName, string? PaidFromAccount);

    private sealed record EffectRow(
        Guid SkuId, decimal Quantity, decimal OnHand, decimal Allocated, decimal Capitalized, decimal Expensed, decimal CostBefore, decimal CostAfter);

    public async Task<Result<LandedCostDetailDto>> Handle(GetLandedCostQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var voucher = await connection.QuerySingleOrDefaultAsync<LandedCostListDto>(new CommandDefinition(
            $"{LandedCostDocuments.ListSelect} WHERE v.id = @Id;", new { request.Id }, cancellationToken: cancellationToken));
        if (voucher is null)
        {
            return Result<LandedCostDetailDto>.Failure(LandedCostDocuments.NotFound);
        }

        var extra = await connection.QuerySingleAsync<(Guid? FxRateId, string? Notes, string? VoidReason)>(new CommandDefinition(
            "SELECT fx_rate_id AS FxRateId, notes AS Notes, void_reason AS VoidReason FROM landed_cost_vouchers WHERE id = @Id;",
            new { request.Id }, cancellationToken: cancellationToken));
        var bills = (await connection.QueryAsync<DocumentLinkDto>(new CommandDefinition(
            """
            SELECT p.id AS Id, p.bill_number AS Number, p.status AS Status, p.bill_date AS Date, p.total_usd AS TotalUsd
            FROM landed_cost_voucher_bills b INNER JOIN purchase_invoices p ON p.id = b.purchase_invoice_id
            WHERE b.voucher_id = @Id ORDER BY p.serial_no;
            """,
            new { request.Id }, cancellationToken: cancellationToken))).ToList();
        var chargeRows = (await connection.QueryAsync<ChargeRow>(new CommandDefinition(
            """
            SELECT c.id AS Id, c.line_number AS LineNumber, c.charge_type AS ChargeType, c.description AS Description, c.amount_usd AS AmountUsd,
                   c.split_method AS SplitMethod, c.supplier_party_id AS SupplierPartyId, COALESCE(NULLIF(pa.display_name_ar, ''), pa.display_name) AS SupplierName,
                   c.paid_from_account AS PaidFromAccount
            FROM landed_cost_charges c LEFT JOIN parties pa ON pa.id = c.supplier_party_id
            WHERE c.voucher_id = @Id ORDER BY c.line_number;
            """,
            new { request.Id }, cancellationToken: cancellationToken))).ToList();
        var serviceBills = (await connection.QueryAsync<(Guid SupplierId, Guid Id, string Number, string Status, DateOnly Date, decimal TotalUsd)>(new CommandDefinition(
            """
            SELECT supplier_party_id AS SupplierId, id AS Id, bill_number AS Number, status AS Status, bill_date AS Date, total_usd AS TotalUsd
            FROM purchase_invoices WHERE landed_cost_voucher_id = @Id;
            """,
            new { request.Id }, cancellationToken: cancellationToken))).ToDictionary(b => b.SupplierId);
        var charges = chargeRows.Select(c => new LandedCostChargeDto(
            c.Id, c.LineNumber, c.ChargeType, c.Description, c.AmountUsd, c.SplitMethod, c.SupplierPartyId, c.SupplierName, c.PaidFromAccount,
            c.SupplierPartyId is { } s && serviceBills.TryGetValue(s, out var b) ? new DocumentLinkDto(b.Id, b.Number, b.Status, b.Date, b.TotalUsd) : null)).ToList();

        var lines = await LandedCostDocuments.LinesAsync(connection, null, request.Id, cancellationToken);
        var isPreview = voucher.Status == "DRAFT";
        IReadOnlyList<ChargeShare> shares;
        IReadOnlyList<EffectRow> effects;
        string? problem = null;
        if (isPreview)
        {
            var onHand = await LandedCostDocuments.OnHandAsync(connection, null, lines.Select(l => l.SkuId), cancellationToken);
            var costs = (await connection.QueryAsync<(Guid Id, decimal Cost)>(new CommandDefinition(
                "SELECT id AS Id, cost_price_usd AS Cost FROM skus WHERE id = ANY(@ids);", new { ids = lines.Select(l => l.SkuId).Distinct().ToArray() },
                cancellationToken: cancellationToken))).ToDictionary(c => c.Id, c => c.Cost);
            var result = LandedCostCalculator.Calculate(
                lines.Select(l => new LandedLine(l.LineId, l.SkuId, l.Quantity, l.NetUnitCost)).ToList(),
                chargeRows.Select(c => new LandedCharge(c.Id, c.AmountUsd, c.SplitMethod)).ToList(), onHand);
            problem = result.IsFailure ? result.Error.Message : null;
            shares = result.IsSuccess ? result.Value!.Shares : [];
            effects = result.IsSuccess
                ? result.Value!.Effects.Select(e =>
                {
                    var before = costs.GetValueOrDefault(e.SkuId);
                    var after = e.OnHand > 0 ? Math.Round((e.OnHand * before + e.Capitalized) / e.OnHand, 4) : before;
                    return new EffectRow(e.SkuId, e.Quantity, e.OnHand, e.Allocated, e.Capitalized, e.Expensed, before, after);
                }).ToList()
                : [];
        }
        else
        {
            shares = (await connection.QueryAsync<ChargeShare>(new CommandDefinition(
                "SELECT charge_id AS ChargeId, purchase_invoice_line_id AS LineId, amount_usd AS Amount FROM landed_cost_allocations WHERE voucher_id = @Id;",
                new { request.Id }, cancellationToken: cancellationToken))).ToList();
            effects = (await connection.QueryAsync<EffectRow>(new CommandDefinition(
                """
                SELECT sku_id AS SkuId, quantity AS Quantity, on_hand AS OnHand, allocated_usd AS Allocated, capitalized_usd AS Capitalized,
                       expensed_usd AS Expensed, cost_before_usd AS CostBefore, cost_after_usd AS CostAfter
                FROM landed_cost_item_effects WHERE voucher_id = @Id;
                """,
                new { request.Id }, cancellationToken: cancellationToken))).ToList();
        }

        var byLine = shares.GroupBy(s => s.LineId).ToDictionary(g => g.Key, g => g.ToDictionary(s => s.ChargeId, s => s.Amount));
        var lineDtos = lines.Select(l =>
        {
            var byCharge = byLine.GetValueOrDefault(l.LineId) ?? [];
            var allocated = byCharge.Values.Sum();
            return new LandedCostLineDto(l.LineId, l.BillNumber, l.ItemCode, l.ItemName, l.Quantity, l.NetUnitCost, allocated,
                l.Quantity > 0 ? Math.Round(l.NetUnitCost + allocated / l.Quantity, 4) : l.NetUnitCost, byCharge);
        }).ToList();
        var names = lines.GroupBy(l => l.SkuId).ToDictionary(g => g.Key, g => (g.First().ItemCode, g.First().ItemName));
        var effectDtos = effects.Select(e => new LandedCostItemEffectDto(
            e.SkuId, names.GetValueOrDefault(e.SkuId).ItemCode ?? string.Empty, names.GetValueOrDefault(e.SkuId).ItemName ?? string.Empty,
            e.Quantity, e.OnHand, e.Allocated, e.Capitalized, e.Expensed, e.CostBefore, e.CostAfter)).ToList();

        return Result<LandedCostDetailDto>.Success(new LandedCostDetailDto(
            voucher, extra.FxRateId, extra.Notes, extra.VoidReason, bills, charges, lineDtos, effectDtos, isPreview, problem));
    }
}

// ------------------------------------------------------------------ shared

internal static class LandedCostDocuments
{
    public static readonly Error NotFound = new("LandedCost.NotFound", "Landed cost voucher was not found.");

    public const string ListSelect = """
        SELECT v.id AS Id, v.voucher_number AS VoucherNumber, v.voucher_date AS VoucherDate, v.status AS Status, v.total_usd AS TotalUsd,
               COALESCE((SELECT string_agg(p.bill_number, '، ' ORDER BY p.serial_no) FROM landed_cost_voucher_bills b
                         INNER JOIN purchase_invoices p ON p.id = b.purchase_invoice_id WHERE b.voucher_id = v.id), '') AS BillNumbers,
               (SELECT count(*)::int FROM landed_cost_charges c WHERE c.voucher_id = v.id) AS ChargeCount
        FROM landed_cost_vouchers v
        """;

    public sealed record Header(Guid Id, string VoucherNumber, string Status, DateOnly VoucherDate, Guid? FxRateId, DateTimeOffset? PostedAt);

    /// <summary>A bill line of the voucher: what is still kept (bought minus posted returns) at its net unit cost.</summary>
    public sealed record Line(Guid LineId, Guid SkuId, string BillNumber, string ItemCode, string ItemName, decimal Quantity, decimal NetUnitCost);

    public sealed record Charge(Guid Id, string ChargeType, string? Description, decimal AmountUsd, string SplitMethod, Guid? SupplierPartyId, string? PaidFromAccount);

    public static Task<Header?> LockAsync(DbConnection connection, DbTransaction transaction, Guid id, CancellationToken ct) =>
        connection.QuerySingleOrDefaultAsync<Header>(new CommandDefinition(
            """
            SELECT id AS Id, voucher_number AS VoucherNumber, status AS Status, voucher_date AS VoucherDate, fx_rate_id AS FxRateId, posted_at AS PostedAt
            FROM landed_cost_vouchers WHERE id = @id FOR UPDATE;
            """,
            new { id }, transaction, cancellationToken: ct));

    public static async Task<IReadOnlyList<Line>> LinesAsync(DbConnection connection, DbTransaction? transaction, Guid voucherId, CancellationToken ct) =>
        (await connection.QueryAsync<Line>(new CommandDefinition(
            """
            SELECT l.id AS LineId, i.sku_id AS SkuId, p.bill_number AS BillNumber, i.part_number AS ItemCode, COALESCE(NULLIF(i.name_ar, ''), i.name_en) AS ItemName,
                   l.quantity - (SELECT COALESCE(SUM(x.quantity), 0) FROM purchase_invoice_lines x INNER JOIN purchase_invoices xp ON xp.id = x.purchase_invoice_id
                                 WHERE x.return_of_line_id = l.id AND xp.status = 'POSTED') AS Quantity,
                   round(l.unit_cost_usd * (1 - l.discount_pct / 100) * CASE WHEN p.subtotal_usd = 0 THEN 1 ELSE p.total_usd / p.subtotal_usd END, 6) AS NetUnitCost
            FROM landed_cost_voucher_bills b
            INNER JOIN purchase_invoices p ON p.id = b.purchase_invoice_id
            INNER JOIN purchase_invoice_lines l ON l.purchase_invoice_id = p.id
            INNER JOIN items i ON i.id = l.item_id
            WHERE b.voucher_id = @voucherId
            ORDER BY p.serial_no, l.line_number;
            """,
            new { voucherId }, transaction, cancellationToken: ct))).ToList();

    public static async Task<IReadOnlyList<Charge>> ChargesAsync(DbConnection connection, DbTransaction transaction, Guid voucherId, CancellationToken ct) =>
        (await connection.QueryAsync<Charge>(new CommandDefinition(
            """
            SELECT id AS Id, charge_type AS ChargeType, description AS Description, amount_usd AS AmountUsd, split_method AS SplitMethod,
                   supplier_party_id AS SupplierPartyId, paid_from_account AS PaidFromAccount
            FROM landed_cost_charges WHERE voucher_id = @voucherId ORDER BY line_number;
            """,
            new { voucherId }, transaction, cancellationToken: ct))).ToList();

    /// <summary>Quantity on hand per SKU in every warehouse — what the landed cost can still be capitalized on.</summary>
    public static async Task<IReadOnlyDictionary<Guid, decimal>> OnHandAsync(DbConnection connection, DbTransaction? transaction, IEnumerable<Guid> skuIds, CancellationToken ct) =>
        (await connection.QueryAsync<(Guid SkuId, decimal OnHand)>(new CommandDefinition(
            "SELECT sku_id AS SkuId, SUM(quantity_on_hand) AS OnHand FROM inventory_stock WHERE sku_id = ANY(@ids) GROUP BY sku_id;",
            new { ids = skuIds.Distinct().ToArray() }, transaction, cancellationToken: ct))).ToDictionary(r => r.SkuId, r => r.OnHand);
}

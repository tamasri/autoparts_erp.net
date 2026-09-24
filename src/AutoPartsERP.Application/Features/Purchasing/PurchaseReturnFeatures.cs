using AutoPartsERP.Application.Common.Pricing;
using Dapper;

namespace AutoPartsERP.Application.Features.Purchasing;

// Purchase returns (مردود مشتريات), as ERPNext's Purchase Invoice with is_return + return_against: made from a posted bill, line by
// line, at the bill's own costs and its share of the bill discount, never more than the bill still holds. The return is a draft
// until posted (PostPurchaseInvoiceCommand), which sends the goods back and credits the bill.

public sealed record GetPurchaseReturnableQuery(Guid BillId) : IRequest<Result<IReadOnlyList<ReturnableLineDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Purchases.Read;
}

public sealed class GetPurchaseReturnableQueryHandler : IRequestHandler<GetPurchaseReturnableQuery, Result<IReadOnlyList<ReturnableLineDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetPurchaseReturnableQueryHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result<IReadOnlyList<ReturnableLineDto>>> Handle(GetPurchaseReturnableQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        return Result<IReadOnlyList<ReturnableLineDto>>.Success(await PurchaseReturnLines.ReadAsync(connection, null, request.BillId, cancellationToken));
    }
}

public sealed record CreatePurchaseReturnCommand(Guid BillId, CreateReturnRequest Request, string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest, IPeriodSensitiveRequest
{
    public string RequiredPermission => PermissionCodes.Purchases.Create;
    public string AuditModule => "PURCHASES";
    public DateTimeOffset OperationDate => Request.ReturnDate.ToDateTime(TimeOnly.MinValue);
    public string Module => PurchaseDocuments.Module;
}

public sealed class CreatePurchaseReturnCommandValidator : AbstractValidator<CreatePurchaseReturnCommand>
{
    public CreatePurchaseReturnCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.BillId).NotEmpty();
        RuleFor(x => x.Request.Reason).MaximumLength(500);
        RuleFor(x => x.Request.Lines).NotEmpty().WithMessage("Choose at least one line to return.");
        RuleForEach(x => x.Request.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.LineId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0);
        });
        RuleFor(x => x.Request.Lines).Must(l => l.Select(x => x.LineId).Distinct().Count() == l.Count).WithMessage("Each line of the bill once.");
    }
}

public sealed class CreatePurchaseReturnCommandHandler : IRequestHandler<CreatePurchaseReturnCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreatePurchaseReturnCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    private sealed record Bill(
        Guid Id, string Status, bool IsReturn, string Kind, Guid SupplierPartyId, string? SupplierRef, Guid? WarehouseId, Guid? FxRateId,
        DateOnly BillDate, decimal SubtotalUsd, decimal DiscountAmountUsd);

    public async Task<Result<Guid>> Handle(CreatePurchaseReturnCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var bill = await connection.QuerySingleOrDefaultAsync<Bill>(new CommandDefinition(
            """
            SELECT id AS Id, status AS Status, is_return AS IsReturn, kind AS Kind, supplier_party_id AS SupplierPartyId, supplier_ref AS SupplierRef,
                   warehouse_id AS WarehouseId, fx_rate_id AS FxRateId, bill_date AS BillDate, subtotal_usd AS SubtotalUsd, discount_amount_usd AS DiscountAmountUsd
            FROM purchase_invoices WHERE id = @BillId FOR UPDATE;
            """,
            new { command.BillId }, transaction, cancellationToken: cancellationToken));
        if (bill is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(PurchaseDocuments.NotFound);
        }

        if (bill.IsReturn || bill.Kind != "GOODS" || bill.Status != "POSTED")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("PurchaseReturn.NotReturnable", "Only a posted goods bill (not a return or a service bill) can be returned."));
        }

        if (request.ReturnDate < bill.BillDate)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("PurchaseReturn.BeforeBill", "A return cannot be dated before the bill it returns."));
        }

        var returnable = (await PurchaseReturnLines.ReadAsync(connection, transaction, bill.Id, cancellationToken)).ToDictionary(l => l.LineId);
        foreach (var line in request.Lines)
        {
            if (!returnable.TryGetValue(line.LineId, out var source))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("PurchaseReturn.LineNotOnBill", "A chosen line is not a line of this bill."));
            }

            if (line.Quantity > source.Returnable)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("PurchaseReturn.ExceedsBought", $"Item {source.Code}: at most {source.Returnable:0.####} can still be returned."));
            }
        }

        // The bill's lines are returned at their own cost and line discount; the bill discount is shared out (DocumentDiscount.ReturnShare).
        var lines = request.Lines.Select(l => (Line: l, Source: returnable[l.LineId])).ToList();
        var linesTotal = lines.Sum(x => Math.Round(x.Line.Quantity * x.Source.UnitPriceUsd * (1 - x.Source.DiscountPct / 100m), 4));
        var alreadyGivenBack = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(SUM(discount_amount_usd), 0) FROM purchase_invoices WHERE return_against_id = @BillId AND status = 'POSTED';",
            new { command.BillId }, transaction, cancellationToken: cancellationToken));
        var returnsEverythingLeft = returnable.Values.All(s => s.Returnable == (request.Lines.FirstOrDefault(l => l.LineId == s.LineId)?.Quantity ?? 0m));
        var discount = DocumentDiscount.ReturnShare(bill.DiscountAmountUsd, bill.SubtotalUsd, linesTotal, alreadyGivenBack, returnsEverythingLeft);

        var id = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO purchase_invoices (
                id, is_return, return_against_id, supplier_party_id, supplier_ref, bill_date, due_date, warehouse_id, status, fx_rate_id,
                subtotal_usd, discount_pct, discount_amount_usd, total_usd, paid_usd, notes, created_by)
            VALUES (
                @id, TRUE, @BillId, @SupplierPartyId, @SupplierRef, @ReturnDate, @ReturnDate, @WarehouseId, 'DRAFT', @FxRateId,
                @Subtotal, NULL, @Discount, @Total, 0, @Reason, @By);
            """,
            new
            {
                id, command.BillId, bill.SupplierPartyId, bill.SupplierRef, request.ReturnDate, bill.WarehouseId, bill.FxRateId,
                Subtotal = -linesTotal, Discount = discount, Total = -(linesTotal - discount), Reason = request.Reason?.Trim(), By = _currentUser.UserId
            },
            transaction, cancellationToken: cancellationToken));

        var number = 0;
        foreach (var (line, source) in lines)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO purchase_invoice_lines (purchase_invoice_id, line_number, item_id, quantity, unit_cost_usd, discount_pct, line_total_usd, return_of_line_id)
                SELECT @id, @n, o.item_id, @Quantity, o.unit_cost_usd, o.discount_pct, round(@Quantity * o.unit_cost_usd * (1 - o.discount_pct / 100), 4), o.id
                FROM purchase_invoice_lines o WHERE o.id = @LineId;
                """,
                new { id, n = ++number, line.Quantity, line.LineId }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(id);
    }
}

/// <summary>A bill's lines with what posted returns took back and what may still be returned.</summary>
internal static class PurchaseReturnLines
{
    public static async Task<IReadOnlyList<ReturnableLineDto>> ReadAsync(DbConnection connection, DbTransaction? transaction, Guid billId, CancellationToken ct) =>
        (await connection.QueryAsync<ReturnableLineDto>(new CommandDefinition(
            """
            SELECT l.id AS LineId, l.line_number AS LineNumber, i.part_number AS Code, COALESCE(NULLIF(i.name_ar, ''), i.name_en) AS Name,
                   l.quantity AS Quantity, r.returned AS Returned, l.quantity - r.returned AS Returnable,
                   l.unit_cost_usd AS UnitPriceUsd, l.discount_pct AS DiscountPct, NULL::uuid AS LocationId
            FROM purchase_invoice_lines l
            INNER JOIN items i ON i.id = l.item_id
            CROSS JOIN LATERAL (
                SELECT COALESCE(SUM(x.quantity), 0) AS returned FROM purchase_invoice_lines x
                INNER JOIN purchase_invoices xp ON xp.id = x.purchase_invoice_id
                WHERE x.return_of_line_id = l.id AND xp.status = 'POSTED') r
            WHERE l.purchase_invoice_id = @billId
            ORDER BY l.line_number;
            """,
            new { billId }, transaction, cancellationToken: ct))).ToList();
}

using Dapper;

namespace AutoPartsERP.Application.Features.Purchasing;

/// <summary>
/// What we owe a supplier, movement by movement in dollars: posted bills raise it (credit), payments and returns lower it (debit).
/// The balance is what is still owed to the supplier (positive = we owe them).
/// </summary>
public sealed record GetSupplierStatementQuery(Guid PartyId)
    : IRequest<Result<SupplierStatementDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Reports.AccountStatement;
}

public sealed class GetSupplierStatementQueryHandler : IRequestHandler<GetSupplierStatementQuery, Result<SupplierStatementDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetSupplierStatementQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private sealed record Row(Guid Id, string Type, DateTime Date, DateTime? DueDate, decimal DebitUsd, decimal CreditUsd, decimal BalanceUsd, string Reference);

    public async Task<Result<SupplierStatementDto>> Handle(GetSupplierStatementQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var isVendor = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM party_type_assignments WHERE party_id = @PartyId AND type_code = 'VENDOR' AND is_active);",
            new { request.PartyId }, cancellationToken: cancellationToken));
        if (!isVendor)
        {
            return Result<SupplierStatementDto>.Failure(new Error("Party.NotASupplier", "This account is not an active supplier."));
        }

        var rows = (await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT id AS Id, type AS Type, occurred_at AS Date, due_date AS DueDate, debit_usd AS DebitUsd, credit_usd AS CreditUsd,
                   SUM(credit_usd - debit_usd) OVER (ORDER BY occurred_at, created_at, id) AS BalanceUsd, reference AS Reference
            FROM (
                -- A bill raises what we owe; a return (a negative bill) lowers it.
                SELECT b.id, CASE WHEN b.is_return THEN 'PURCHASE_RETURN' ELSE 'BILL' END AS type, b.bill_date::timestamp AS occurred_at, b.created_at,
                       CASE WHEN b.is_return THEN NULL ELSE b.due_date::timestamp END AS due_date,
                       GREATEST(-b.total_usd, 0) AS debit_usd, GREATEST(b.total_usd, 0) AS credit_usd, b.bill_number AS reference
                FROM purchase_invoices b WHERE b.supplier_party_id = @PartyId AND b.status = 'POSTED'
                UNION ALL
                SELECT p.id, 'SUPPLIER_PAYMENT', p.payment_date::timestamp, p.created_at, NULL::timestamp,
                       p.amount_usd, 0::numeric, p.payment_number
                FROM supplier_payments p WHERE p.supplier_party_id = @PartyId AND NOT p.is_reversed
            ) t
            ORDER BY occurred_at, created_at, id;
            """,
            new { request.PartyId }, cancellationToken: cancellationToken))).ToArray();

        var transactions = rows.Select(r => new CustomerStatementTransactionDto(
            r.Id, r.Type, r.Date, r.DueDate, 0, 0, r.DebitUsd, r.CreditUsd, 0, r.BalanceUsd, string.Empty, r.Reference)).ToArray();
        var billed = rows.Sum(r => r.CreditUsd);
        var paid = rows.Sum(r => r.DebitUsd);
        return Result<SupplierStatementDto>.Success(new SupplierStatementDto(request.PartyId, billed, paid, billed - paid, transactions));
    }
}

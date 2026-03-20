namespace AutoPartsERP.Application.Features.Parties.GetPartyCombinedStatement;

public sealed record GetPartyCombinedStatementQuery(Guid PartyId)
    : IRequest<Result<CombinedStatementDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Reports.AccountStatement;
}

public sealed class GetPartyCombinedStatementQueryHandler : IRequestHandler<GetPartyCombinedStatementQuery, Result<CombinedStatementDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetPartyCombinedStatementQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<CombinedStatementDto>> Handle(GetPartyCombinedStatementQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var hasCombinedStatement = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT
                EXISTS (
                    SELECT 1
                    FROM party_type_assignments
                    WHERE party_id = @PartyId
                      AND type_code = 'CUSTOMER'
                      AND is_active = TRUE
                )
                AND
                EXISTS (
                    SELECT 1
                    FROM party_type_assignments
                    WHERE party_id = @PartyId
                      AND type_code = 'VENDOR'
                      AND is_active = TRUE
                );
            """,
            new { request.PartyId },
            cancellationToken: cancellationToken));

        if (!hasCombinedStatement)
        {
            return Result<CombinedStatementDto>.Failure(
                new Error("Party.NoCombinedStatement", "Combined statement requires both active CUSTOMER and VENDOR types."));
        }

        var arLines = (await connection.QueryAsync<ArLine>(new CommandDefinition(
            """
            SELECT
                i.invoice_date AS Date,
                'INVOICE' AS EntryType,
                COALESCE(i.invoice_number, i.id::text) AS ReferenceNumber,
                COALESCE(i.notes, 'Invoice') AS Description,
                i.total_syp AS DebitSyp,
                0::numeric AS CreditSyp,
                i.total_usd AS DebitUsd,
                0::numeric AS CreditUsd
            FROM invoices i
            INNER JOIN customers c ON c.id = i.customer_id
            WHERE c.party_id = @PartyId
            UNION ALL
            SELECT
                p.payment_date AS Date,
                'PAYMENT' AS EntryType,
                COALESCE(p.payment_number, p.id::text) AS ReferenceNumber,
                COALESCE(p.notes, 'Payment') AS Description,
                0::numeric AS DebitSyp,
                p.amount_syp AS CreditSyp,
                0::numeric AS DebitUsd,
                p.amount_usd AS CreditUsd
            FROM payments p
            INNER JOIN customers c ON c.id = p.customer_id
            WHERE c.party_id = @PartyId
            ORDER BY Date;
            """,
            new { request.PartyId },
            cancellationToken: cancellationToken))).ToArray();

        var arBalance = new CombinedStatementBalanceDto(
            arLines.Sum(x => x.DebitSyp),
            arLines.Sum(x => x.CreditSyp),
            arLines.Sum(x => x.DebitSyp) - arLines.Sum(x => x.CreditSyp),
            arLines.Sum(x => x.DebitUsd),
            arLines.Sum(x => x.CreditUsd),
            arLines.Sum(x => x.DebitUsd) - arLines.Sum(x => x.CreditUsd));

        var apBalance = new CombinedStatementBalanceDto(0m, 0m, 0m, 0m, 0m, 0m);
        var netPosition = new CombinedStatementBalanceDto(
            arBalance.TotalDebitSyp - apBalance.TotalDebitSyp,
            arBalance.TotalCreditSyp - apBalance.TotalCreditSyp,
            arBalance.OutstandingSyp - apBalance.OutstandingSyp,
            arBalance.TotalDebitUsd - apBalance.TotalDebitUsd,
            arBalance.TotalCreditUsd - apBalance.TotalCreditUsd,
            arBalance.OutstandingUsd - apBalance.OutstandingUsd);

        var dto = new CombinedStatementDto(
            request.PartyId,
            arLines.Select(x => new CombinedStatementLineDto(
                x.Date,
                x.EntryType,
                x.ReferenceNumber,
                x.Description,
                x.DebitSyp,
                x.CreditSyp,
                x.DebitUsd,
                x.CreditUsd)).ToArray(),
            Array.Empty<CombinedStatementLineDto>(),
            arBalance,
            apBalance,
            netPosition);

        return Result<CombinedStatementDto>.Success(dto);
    }

    private sealed class ArLine
    {
        public DateOnly Date { get; init; }
        public string EntryType { get; init; } = string.Empty;
        public string ReferenceNumber { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public decimal DebitSyp { get; init; }
        public decimal CreditSyp { get; init; }
        public decimal DebitUsd { get; init; }
        public decimal CreditUsd { get; init; }
    }
}

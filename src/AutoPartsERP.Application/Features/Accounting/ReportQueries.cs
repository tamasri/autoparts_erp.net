using AutoPartsERP.Contracts.Accounting;

namespace AutoPartsERP.Application.Features.Accounting;

// The financial reports. Each reads the ledger totals from ERPNext (the ledger's system of record) and shapes them with FinancialReports;
// nothing is stored here, so a report is always exactly what the ledger holds now.

public sealed record GetTrialBalanceQuery(DateOnly From, DateOnly To, bool IncludeZero) : IRequest<Result<TrialBalanceDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetTrialBalanceQueryValidator : AbstractValidator<GetTrialBalanceQuery>
{
    public GetTrialBalanceQueryValidator() => RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("The end date is before the start date.");
}

public sealed class GetTrialBalanceQueryHandler : IRequestHandler<GetTrialBalanceQuery, Result<TrialBalanceDto>>
{
    private readonly IErpNextClient _erpNext;

    public GetTrialBalanceQueryHandler(IErpNextClient erpNext) { _erpNext = erpNext; }

    public async Task<Result<TrialBalanceDto>> Handle(GetTrialBalanceQuery request, CancellationToken cancellationToken)
    {
        var chart = await _erpNext.GetChartOfAccountsAsync(cancellationToken);
        if (chart.IsFailure) return Result<TrialBalanceDto>.Failure(chart.Error);
        var before = await _erpNext.GetGlBalancesAsync(null, request.From.AddDays(-1), cancellationToken);
        if (before.IsFailure) return Result<TrialBalanceDto>.Failure(before.Error);
        var period = await _erpNext.GetGlBalancesAsync(request.From, request.To, cancellationToken);
        if (period.IsFailure) return Result<TrialBalanceDto>.Failure(period.Error);

        return Result<TrialBalanceDto>.Success(FinancialReports.TrialBalance(ChartTree.Build(chart.Value!), before.Value!, period.Value!, request.From, request.To, request.IncludeZero));
    }
}

public sealed record GetBalanceSheetQuery(DateOnly AsOf) : IRequest<Result<BalanceSheetDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetBalanceSheetQueryHandler : IRequestHandler<GetBalanceSheetQuery, Result<BalanceSheetDto>>
{
    private readonly IErpNextClient _erpNext;

    public GetBalanceSheetQueryHandler(IErpNextClient erpNext) { _erpNext = erpNext; }

    public async Task<Result<BalanceSheetDto>> Handle(GetBalanceSheetQuery request, CancellationToken cancellationToken)
    {
        var chart = await _erpNext.GetChartOfAccountsAsync(cancellationToken);
        if (chart.IsFailure) return Result<BalanceSheetDto>.Failure(chart.Error);
        var cumulative = await _erpNext.GetGlBalancesAsync(null, request.AsOf, cancellationToken);
        if (cumulative.IsFailure) return Result<BalanceSheetDto>.Failure(cumulative.Error);

        return Result<BalanceSheetDto>.Success(FinancialReports.BalanceSheet(ChartTree.Build(chart.Value!), cumulative.Value!, request.AsOf));
    }
}

public sealed record GetProfitLossStatementQuery(DateOnly From, DateOnly To) : IRequest<Result<ProfitLossDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetProfitLossStatementQueryValidator : AbstractValidator<GetProfitLossStatementQuery>
{
    public GetProfitLossStatementQueryValidator() => RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("The end date is before the start date.");
}

public sealed class GetProfitLossStatementQueryHandler : IRequestHandler<GetProfitLossStatementQuery, Result<ProfitLossDto>>
{
    private readonly IErpNextClient _erpNext;

    public GetProfitLossStatementQueryHandler(IErpNextClient erpNext) { _erpNext = erpNext; }

    public async Task<Result<ProfitLossDto>> Handle(GetProfitLossStatementQuery request, CancellationToken cancellationToken)
    {
        var chart = await _erpNext.GetChartOfAccountsAsync(cancellationToken);
        if (chart.IsFailure) return Result<ProfitLossDto>.Failure(chart.Error);
        var period = await _erpNext.GetGlBalancesAsync(request.From, request.To, cancellationToken);
        if (period.IsFailure) return Result<ProfitLossDto>.Failure(period.Error);

        return Result<ProfitLossDto>.Success(FinancialReports.ProfitLoss(ChartTree.Build(chart.Value!), period.Value!, request.From, request.To));
    }
}

// ------------------------------------------------------------------ ledger statement

/// <summary>
/// One page of the postings on a ledger account (optionally one party, optionally only vouchers carrying a tag), with a running balance that is
/// right on every page. The totals and the closing balance always cover the whole period; only the rows are paged.
/// </summary>
public sealed record GetLedgerStatementQuery(string Account, DateOnly From, DateOnly To, string? Party, Guid? TagId, int PageNumber = 1, int PageSize = LedgerPaging.DefaultPageSize)
    : IRequest<Result<LedgerStatementDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetLedgerStatementQueryValidator : AbstractValidator<GetLedgerStatementQuery>
{
    public GetLedgerStatementQueryValidator()
    {
        RuleFor(x => x.Account).NotEmpty();
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("The end date is before the start date.");
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
    }
}

public sealed class GetLedgerStatementQueryHandler : IRequestHandler<GetLedgerStatementQuery, Result<LedgerStatementDto>>
{
    /// <summary>A tag can be filtered on only while its vouchers fit in one ERPNext query.</summary>
    public const int MaxTaggedVouchers = 300;

    private readonly IErpNextClient _erpNext;
    private readonly IDbConnectionFactory _connectionFactory;

    public GetLedgerStatementQueryHandler(IErpNextClient erpNext, IDbConnectionFactory connectionFactory)
    {
        _erpNext = erpNext;
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<LedgerStatementDto>> Handle(GetLedgerStatementQuery request, CancellationToken cancellationToken)
    {
        var chart = await _erpNext.GetChartOfAccountsAsync(cancellationToken);
        if (chart.IsFailure) return Result<LedgerStatementDto>.Failure(chart.Error);
        var account = chart.Value!.FirstOrDefault(a => a.Name == request.Account);
        if (account is null) return Result<LedgerStatementDto>.Failure(new Error("Accounting.NotFound", "The account was not found."));
        if (account.IsGroup) return Result<LedgerStatementDto>.Failure(new Error("Accounting.NotLedger", "Choose a ledger account, not a group."));

        var sign = ChartTree.IsDebitNormal(account.RootType) ? 1m : -1m;
        var party = string.IsNullOrWhiteSpace(request.Party) ? null : request.Party.Trim();
        var size = LedgerPaging.ClampPageSize(request.PageSize);
        var page = Math.Max(request.PageNumber, 1);
        var offset = LedgerPaging.Offset(page, size);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        // Only the vouchers carrying the tag. The statement is then a list of those lines with its own running total: the account's
        // balance before them means nothing, so the opening is 0.
        IReadOnlyList<string>? tagged = null;
        if (request.TagId is { } tagId)
        {
            tagged = await TagResolver.VouchersForTagAsync(connection, tagId, cancellationToken);
            if (tagged.Count > MaxTaggedVouchers)
            {
                return Result<LedgerStatementDto>.Failure(new Error("Accounting.TooManyTagged", $"The tag is on more than {MaxTaggedVouchers} vouchers; narrow the dates or pick another tag."));
            }

            if (tagged.Count == 0)
            {
                return Result<LedgerStatementDto>.Success(new LedgerStatementDto(account.Name, account.RootType, request.From, request.To, 0, [], 0, 0, 0, 0, page, size, true));
            }
        }

        // Opening = everything before the first day, exact (no row cap), for the whole account or for one party.
        decimal opening = 0;
        if (tagged is null)
        {
            var before = await _erpNext.GetGlSummaryAsync(new ErpNextGlFilter(account.Name, null, party, null, request.From.AddDays(-1), 0), cancellationToken);
            if (before.IsFailure) return Result<LedgerStatementDto>.Failure(before.Error);
            opening = before.Value!.Debit - before.Value!.Credit;
        }

        var period = new ErpNextGlFilter(account.Name, null, party, request.From, request.To, size, offset, tagged);
        var totals = await _erpNext.GetGlSummaryAsync(period, cancellationToken);
        if (totals.IsFailure) return Result<LedgerStatementDto>.Failure(totals.Error);
        var entries = await _erpNext.GetGlEntriesAsync(period, cancellationToken);
        if (entries.IsFailure) return Result<LedgerStatementDto>.Failure(entries.Error);
        var pageEntries = entries.Value!.Take(size).ToList();

        var skipped = new ErpNextGlSummary(0, 0, 0);
        if (offset > 0)
        {
            var before = await _erpNext.GetGlOffsetSummaryAsync(period, offset, cancellationToken);
            if (before.IsFailure) return Result<LedgerStatementDto>.Failure(before.Error);
            skipped = before.Value!;
        }

        var vouchers = pageEntries.Where(e => e.VoucherType is not null && e.VoucherNo is not null).Select(e => (e.VoucherType!, e.VoucherNo!)).Distinct().ToList();
        var tags = await TagResolver.ForVouchersAsync(connection, vouchers, cancellationToken);
        var names = vouchers.Select(v => v.Item2).Distinct().ToArray();
        var links = (await connection.QueryAsync<(string Doctype, string Name, string Type, Guid Id)>(new CommandDefinition(
            """
            SELECT erpnext_doctype AS Doctype, erpnext_name AS Name, local_entity_type AS Type, local_entity_id AS Id
            FROM erpnext_sync_log WHERE erpnext_name = ANY(@names) AND status IN ('SYNCED', 'CANCELLED');
            """,
            new { names }, cancellationToken: cancellationToken))).GroupBy(l => TagResolver.VoucherKey(l.Doctype, l.Name)).ToDictionary(g => g.Key, g => g.First());

        var start = LedgerPaging.PageStartBalance(sign, opening, skipped.Debit, skipped.Credit);
        var balances = LedgerPaging.RunningBalances(sign, start, pageEntries.Select(e => (e.Debit, e.Credit)));
        var rows = new List<LedgerRowDto>(pageEntries.Count);
        for (var i = 0; i < pageEntries.Count; i++)
        {
            var e = pageEntries[i];
            var key = e.VoucherType is not null && e.VoucherNo is not null ? TagResolver.VoucherKey(e.VoucherType, e.VoucherNo) : null;
            var link = key is not null ? links.GetValueOrDefault(key) : default;
            rows.Add(new LedgerRowDto(e.Name, e.PostingDate, e.VoucherType, e.VoucherNo, e.Party, e.Remarks, e.Debit, e.Credit, balances[i],
                link.Type, link.Type is null ? null : link.Id, key is not null && tags.TryGetValue(key, out var t) ? t : []));
        }

        return Result<LedgerStatementDto>.Success(new LedgerStatementDto(
            account.Name, account.RootType, request.From, request.To, sign * opening, rows, totals.Value!.Debit, totals.Value!.Credit,
            LedgerPaging.ClosingBalance(sign, opening, totals.Value!.Debit, totals.Value!.Credit), totals.Value!.Count, page, size, tagged is not null));
    }
}

// ------------------------------------------------------------------ receivables and payables

/// <summary>Who owes us (customers) or whom we owe (suppliers), with the unpaid invoices aged by how late they are.</summary>
public sealed record GetPartyBalancesQuery(string PartyType, DateOnly AsOf) : IRequest<Result<PartyBalancesDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetPartyBalancesQueryValidator : AbstractValidator<GetPartyBalancesQuery>
{
    public GetPartyBalancesQueryValidator() =>
        RuleFor(x => x.PartyType).Must(t => t is PartyTypeCodes.Customer or PartyTypeCodes.Vendor).WithMessage("Party type must be CUSTOMER or VENDOR.");
}

public sealed class GetPartyBalancesQueryHandler : IRequestHandler<GetPartyBalancesQuery, Result<PartyBalancesDto>>
{
    private readonly IErpNextClient _erpNext;
    private readonly IDbConnectionFactory _connectionFactory;

    public GetPartyBalancesQueryHandler(IErpNextClient erpNext, IDbConnectionFactory connectionFactory)
    {
        _erpNext = erpNext;
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PartyBalancesDto>> Handle(GetPartyBalancesQuery request, CancellationToken cancellationToken)
    {
        var isCustomer = request.PartyType == PartyTypeCodes.Customer;
        var partyDoctype = isCustomer ? "Customer" : "Supplier";
        var balances = await _erpNext.GetPartyBalancesAsync(partyDoctype, request.AsOf, cancellationToken);
        if (balances.IsFailure) return Result<PartyBalancesDto>.Failure(balances.Error);
        var open = await _erpNext.GetOpenInvoicesAsync(isCustomer ? "Sales Invoice" : "Purchase Invoice", request.AsOf, cancellationToken);
        if (open.IsFailure) return Result<PartyBalancesDto>.Failure(open.Error);

        var openByParty = open.Value!.ToLookup(i => i.Party, StringComparer.Ordinal);
        var names = balances.Value!.Select(b => b.Party).Concat(open.Value!.Select(i => i.Party)).Distinct(StringComparer.Ordinal).ToArray();

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var partyIds = (await connection.QueryAsync<(string Name, Guid Id)>(new CommandDefinition(
            """
            SELECT erpnext_name AS Name, local_entity_id AS Id FROM erpnext_sync_log
            WHERE local_entity_type = 'Party' AND erpnext_doctype = @partyDoctype AND erpnext_name = ANY(@names) AND status = 'SYNCED';
            """,
            new { partyDoctype, names }, cancellationToken: cancellationToken))).ToDictionary(x => x.Name, x => x.Id, StringComparer.Ordinal);
        var ids = partyIds.Values.ToArray();
        var customerIds = (await connection.QueryAsync<(Guid PartyId, Guid Id)>(new CommandDefinition(
            "SELECT party_id AS PartyId, id AS Id FROM customers WHERE party_id = ANY(@ids);", new { ids }, cancellationToken: cancellationToken)))
            .GroupBy(x => x.PartyId).ToDictionary(g => g.Key, g => g.First().Id);

        var rows = new List<PartyBalanceDto>();
        foreach (var name in names)
        {
            var gl = balances.Value!.FirstOrDefault(b => b.Party == name);
            var balance = isCustomer ? (gl?.Debit ?? 0) - (gl?.Credit ?? 0) : (gl?.Credit ?? 0) - (gl?.Debit ?? 0);
            var invoices = openByParty[name].ToList();
            if (Math.Abs(balance) < 0.005m && invoices.Count == 0) continue;

            decimal Bucket(int min, int max) => invoices.Where(i => Late(i, request.AsOf) >= min && Late(i, request.AsOf) <= max).Sum(i => i.Outstanding);
            var current = invoices.Where(i => Late(i, request.AsOf) <= 0).Sum(i => i.Outstanding);
            var partyId = partyIds.TryGetValue(name, out var pid) ? pid : (Guid?)null;
            rows.Add(new PartyBalanceDto(name, partyId, partyId is { } p && customerIds.TryGetValue(p, out var cid) ? cid : null, balance, current,
                Bucket(1, 30), Bucket(31, 60), Bucket(61, 90), Bucket(91, int.MaxValue), balance - invoices.Sum(i => i.Outstanding)));
        }

        var ordered = rows.OrderByDescending(r => r.Balance).ToList();
        return Result<PartyBalancesDto>.Success(new PartyBalancesDto(request.PartyType, request.AsOf, ordered, ordered.Sum(r => r.Balance)));
    }

    /// <summary>Days past due at the report date (an invoice with no due date ages from its posting date).</summary>
    private static int Late(ErpNextOpenInvoice invoice, DateOnly asOf) => asOf.DayNumber - (invoice.DueDate ?? invoice.PostingDate).DayNumber;
}

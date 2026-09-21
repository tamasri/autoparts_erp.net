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

/// <summary>Every posting on one ledger account (optionally one party, optionally only tagged vouchers) with a running balance.</summary>
public sealed record GetLedgerStatementQuery(string Account, DateOnly From, DateOnly To, string? Party, Guid? TagId, int PageNumber = 1, int PageSize = 100) : IRequest<Result<LedgerStatementDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetLedgerStatementQueryValidator : AbstractValidator<GetLedgerStatementQuery>
{
    public GetLedgerStatementQueryValidator()
    {
        RuleFor(x => x.Account).NotEmpty();
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("The end date is before the start date.");
    }
}

public sealed class GetLedgerStatementQueryHandler : IRequestHandler<GetLedgerStatementQuery, Result<LedgerStatementDto>>
{
    public const int MaxRows = 5000;

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
        var page = Math.Max(request.PageNumber, 1);
        var size = Math.Clamp(request.PageSize, 10, 200);
        var limitStart = (page - 1) * size;

        // Opening = everything before the first day. With a party filter the totals per account cannot be used, so the party's own lines are summed.
        decimal opening;
        if (party is null)
        {
            var before = await _erpNext.GetGlBalancesAsync(null, request.From.AddDays(-1), cancellationToken);
            if (before.IsFailure) return Result<LedgerStatementDto>.Failure(before.Error);
            opening = before.Value!.Where(b => b.Account == account.Name).Sum(b => b.Debit - b.Credit);
        }
        else
        {
            var earlier = await _erpNext.GetGlEntriesAsync(new ErpNextGlFilter(account.Name, null, party, null, request.From.AddDays(-1), MaxRows * 4, 0), cancellationToken);
            if (earlier.IsFailure) return Result<LedgerStatementDto>.Failure(earlier.Error);
            opening = earlier.Value!.Sum(e => e.Debit - e.Credit);
        }

        // Fetch one extra row to detect that there are more pages
        var allEntries = await _erpNext.GetGlEntriesAsync(new ErpNextGlFilter(account.Name, null, party, request.From, request.To, size + 1, limitStart), cancellationToken);
        if (allEntries.IsFailure) return Result<LedgerStatementDto>.Failure(allEntries.Error);
        var totalCount = -1; // unknown total — signals "no paging info"
        var truncated = allEntries.Value!.Count > size;
        var pageEntries = allEntries.Value!.Take(size).ToList();

        // If this is the first page, we can count the total by fetching with a large limit
        // to detect whether there are more rows beyond MaxRows
        if (page == 1)
        {
            var countProbe = await _erpNext.GetGlEntriesAsync(new ErpNextGlFilter(account.Name, null, party, request.From, request.To, MaxRows + 1, 0), cancellationToken);
            if (!countProbe.IsFailure)
            {
                totalCount = countProbe.Value!.Count;
            }
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var vouchers = pageEntries.Where(e => e.VoucherType is not null && e.VoucherNo is not null).Select(e => (e.VoucherType!, e.VoucherNo!)).Distinct().ToList();
        var tags = await TagResolver.ForVouchersAsync(connection, vouchers, cancellationToken);
        var names = vouchers.Select(v => v.Item2).Distinct().ToArray();
        var links = (await connection.QueryAsync<(string Doctype, string Name, string Type, Guid Id)>(new CommandDefinition(
            """
            SELECT erpnext_doctype AS Doctype, erpnext_name AS Name, local_entity_type AS Type, local_entity_id AS Id
            FROM erpnext_sync_log WHERE erpnext_name = ANY(@names) AND status IN ('SYNCED', 'CANCELLED');
            """,
            new { names }, cancellationToken: cancellationToken))).GroupBy(l => TagResolver.VoucherKey(l.Doctype, l.Name)).ToDictionary(g => g.Key, g => g.First());

        // Compute the running balance at the start of this page.
        // For page 1 it is the opening balance; for later pages we must sum all debit-credit from prior pages.
        decimal pageStartBalance;
        if (page == 1)
        {
            pageStartBalance = sign * opening;
        }
        else
        {
            // Fetch all rows before this page to compute their net movement
            var priorEntries = await _erpNext.GetGlEntriesAsync(new ErpNextGlFilter(account.Name, null, party, request.From, request.To, size, 0), cancellationToken);
            if (priorEntries.IsFailure) return Result<LedgerStatementDto>.Failure(priorEntries.Error);
            var net = priorEntries.Value!.Sum(e => sign * (e.Debit - e.Credit));
            pageStartBalance = sign * opening + net;
        }

        var balance = pageStartBalance;
        var rows = new List<LedgerRowDto>(pageEntries.Count);
        foreach (var e in pageEntries)
        {
            balance += sign * (e.Debit - e.Credit);
            var key = e.VoucherType is not null && e.VoucherNo is not null ? TagResolver.VoucherKey(e.VoucherType, e.VoucherNo) : null;
            var link = key is not null ? links.GetValueOrDefault(key) : default;
            rows.Add(new LedgerRowDto(e.Name, e.PostingDate, e.VoucherType, e.VoucherNo, e.Party, e.Remarks, e.Debit, e.Credit, balance,
                link.Type, link.Type is null ? null : link.Id, key is not null && tags.TryGetValue(key, out var t) ? t : []));
        }

        var shown = request.TagId is { } tagId ? rows.Where(r => r.Tags.Any(t => t.Id == tagId)).ToList() : rows;
        return Result<LedgerStatementDto>.Success(new LedgerStatementDto(
            account.Name, account.RootType, request.From, request.To, sign * opening, shown, shown.Sum(r => r.Debit), shown.Sum(r => r.Credit),
            balance, truncated, totalCount, page, size));
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

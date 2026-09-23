using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoPartsERP.Infrastructure.Services;

/// <summary>Accounting side of the ERPNext hand-off: chart maintenance, manual journal entries and the ledger reads the financial reports are built from.</summary>
public sealed partial class ErpNextClient
{
    private const string AccountNumberMethod = "api/method/erpnext.accounts.doctype.account.account.update_account_number";

    // ------------------------------------------------------------------ chart maintenance

    public async Task<Result<string>> CreateAccountAsync(ErpNextAccountCreate account, CancellationToken cancellationToken = default)
    {
        var company = await GetCompanyAsync(cancellationToken);
        if (company.IsFailure)
        {
            return Result<string>.Failure(company.Error);
        }

        var payload = new JsonObject
        {
            ["account_name"] = account.AccountName,
            ["company"] = company.Value!.Name,
            ["parent_account"] = account.ParentAccount,
            ["is_group"] = account.IsGroup ? 1 : 0
        };
        if (!string.IsNullOrWhiteSpace(account.AccountType) && !account.IsGroup)
        {
            payload["account_type"] = account.AccountType;
        }

        if (!string.IsNullOrWhiteSpace(account.AccountNumber))
        {
            payload["account_number"] = account.AccountNumber;
        }

        var response = await _httpClient.PostAsJsonAsync("api/resource/Account", payload, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return Result<string>.Success(await ExtractNameAsync(response, cancellationToken) ?? account.AccountName);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return Result<string>.Failure(new Error("ErpNext.AccountCreateFailed", $"{Explain(response.StatusCode, body)}"));
    }

    public async Task<Result<string>> UpdateAccountAsync(string name, ErpNextAccountUpdate update, CancellationToken cancellationToken = default)
    {
        var current = name;
        if (!string.IsNullOrWhiteSpace(update.AccountName))
        {
            // ERPNext renames an account through its own method, which needs the account number again or the number is dropped.
            var existing = await GetAccountFieldsAsync(name, cancellationToken);
            var renamed = await _httpClient.PostAsJsonAsync(
                AccountNumberMethod,
                new JsonObject { ["name"] = name, ["account_name"] = update.AccountName.Trim(), ["account_number"] = existing?.Number },
                cancellationToken);
            if (!renamed.IsSuccessStatusCode)
            {
                var body = await renamed.Content.ReadAsStringAsync(cancellationToken);
                return Result<string>.Failure(new Error("ErpNext.AccountUpdateFailed", $"{Explain(renamed.StatusCode, body)}"));
            }

            await using var stream = await renamed.Content.ReadAsStreamAsync(cancellationToken);
            var document = await JsonSerializer.DeserializeAsync<JsonDocument>(stream, cancellationToken: cancellationToken);
            if (document!.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(message.GetString()))
            {
                current = message.GetString()!;
            }
        }

        var fields = new JsonObject();
        if (!string.IsNullOrWhiteSpace(update.AccountType))
        {
            fields["account_type"] = update.AccountType;
        }

        if (update.Disabled.HasValue)
        {
            fields["disabled"] = update.Disabled.Value ? 1 : 0;
        }

        if (fields.Count == 0)
        {
            return Result<string>.Success(current);
        }

        var response = await _httpClient.PutAsJsonAsync($"api/resource/Account/{Uri.EscapeDataString(current)}", fields, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return Result<string>.Success(current);
        }

        var failure = await response.Content.ReadAsStringAsync(cancellationToken);
        return Result<string>.Failure(new Error("ErpNext.AccountUpdateFailed", $"{Explain(response.StatusCode, failure)}"));
    }

    private async Task<(string? Number, string? Type)?> GetAccountFieldsAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync($"api/resource/Account/{Uri.EscapeDataString(name)}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var data = (await JsonSerializer.DeserializeAsync<JsonDocument>(stream, cancellationToken: cancellationToken))!.RootElement.GetProperty("data");
            return (Text(data, "account_number"), Text(data, "account_type"));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or HttpRequestException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ manual journal entries

    public async Task<Result<string>> SyncJournalEntryAsync(ErpNextJournalEntrySync entry, CancellationToken cancellationToken = default)
    {
        if (entry.Lines.Count < 2)
        {
            return Result<string>.Failure(new Error("ErpNext.NoLines", "A journal entry needs at least two lines."));
        }

        var company = await GetCompanyAsync(cancellationToken);
        if (company.IsFailure)
        {
            return Result<string>.Failure(company.Error);
        }

        var lines = new JsonArray();
        foreach (var line in entry.Lines)
        {
            var row = new JsonObject
            {
                ["account"] = line.Account,
                ["debit_in_account_currency"] = line.Debit,
                ["credit_in_account_currency"] = line.Credit
            };
            if (!string.IsNullOrWhiteSpace(line.Party))
            {
                row["party_type"] = line.PartyType;
                row["party"] = line.Party;
            }

            if (!string.IsNullOrWhiteSpace(line.Remark))
            {
                row["user_remark"] = line.Remark;
            }

            lines.Add(row);
        }

        var date = entry.Date.ToString("yyyy-MM-dd");
        var doc = new JsonObject
        {
            ["voucher_type"] = entry.VoucherType,
            ["company"] = company.Value!.Name,
            ["posting_date"] = date,
            ["user_remark"] = string.IsNullOrWhiteSpace(entry.Narration) ? $"AutoPartsERP entry {entry.EntryNumber}" : $"{entry.EntryNumber}: {entry.Narration}",
            // A Bank Entry is refused without a reference; every entry carries our number so it can be traced back.
            ["cheque_no"] = string.IsNullOrWhiteSpace(entry.ReferenceNo) ? entry.EntryNumber : entry.ReferenceNo,
            ["cheque_date"] = date,
            ["accounts"] = lines,
            ["docstatus"] = 1
        };
        if (entry.IsOpening)
        {
            doc["is_opening"] = "Yes";
        }

        return await UpsertAsync("Journal Entry", entry.LocalId.ToString(), doc, cancellationToken);
    }

    // ------------------------------------------------------------------ ledger reads

    public async Task<Result<IReadOnlyList<ErpNextGlBalance>>> GetGlBalancesAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var filters = await LedgerFiltersAsync(from, to, cancellationToken);
        if (filters.IsFailure)
        {
            return Result<IReadOnlyList<ErpNextGlBalance>>.Failure(filters.Error);
        }

        var rows = await QueryRowsAsync("GL Entry", ["account", Aggregate.Sum("debit"), Aggregate.Sum("credit")], filters.Value!, null, "account", 0, cancellationToken);
        return rows.IsFailure
            ? Result<IReadOnlyList<ErpNextGlBalance>>.Failure(rows.Error)
            : Result<IReadOnlyList<ErpNextGlBalance>>.Success(rows.Value!.Where(r => Text(r, "account") is not null).Select(r => new ErpNextGlBalance(Text(r, "account")!, Dec(r, "debit"), Dec(r, "credit"))).ToList());
    }

    /// <summary>The order every paged ledger read uses; the name breaks ties so two queries never disagree about which line comes first.</summary>
    private const string LedgerOrder = "posting_date asc, creation asc, name asc";
    private const int OffsetChunk = 2000;
    private const int MaxOffsetScan = 200_000;

    public async Task<Result<IReadOnlyList<ErpNextGlEntry>>> GetGlEntriesAsync(ErpNextGlFilter filter, CancellationToken cancellationToken = default)
    {
        var filters = await GlFiltersAsync(filter, cancellationToken);
        if (filters.IsFailure)
        {
            return Result<IReadOnlyList<ErpNextGlEntry>>.Failure(filters.Error);
        }

        var rows = await QueryRowsAsync(
            "GL Entry",
            ["name", "posting_date", "account", "party_type", "party", "debit", "credit", "voucher_type", "voucher_no", "remarks"],
            filters.Value!, LedgerOrder, null, filter.Limit + 1, cancellationToken, filter.LimitStart);
        if (rows.IsFailure)
        {
            return Result<IReadOnlyList<ErpNextGlEntry>>.Failure(rows.Error);
        }

        return Result<IReadOnlyList<ErpNextGlEntry>>.Success(rows.Value!
            .Where(r => Text(r, "name") is not null && Date(r, "posting_date") is not null)
            .Select(r => new ErpNextGlEntry(
                Text(r, "name")!, Date(r, "posting_date")!.Value, Text(r, "account") ?? string.Empty, Text(r, "party_type"), Text(r, "party"),
                Dec(r, "debit"), Dec(r, "credit"), Text(r, "voucher_type"), Text(r, "voucher_no"), Text(r, "remarks")))
            .ToList());
    }

    public async Task<Result<ErpNextGlSummary>> GetGlSummaryAsync(ErpNextGlFilter filter, CancellationToken cancellationToken = default)
    {
        var filters = await GlFiltersAsync(filter, cancellationToken);
        if (filters.IsFailure)
        {
            return Result<ErpNextGlSummary>.Failure(filters.Error);
        }

        // No group_by: the aggregate query answers with a single row for everything that matches.
        var rows = await QueryRowsAsync("GL Entry", [Aggregate.Count("name", "n"), Aggregate.Sum("debit"), Aggregate.Sum("credit")], filters.Value!, null, null, 0, cancellationToken);
        if (rows.IsFailure)
        {
            return Result<ErpNextGlSummary>.Failure(rows.Error);
        }

        var row = rows.Value!.FirstOrDefault();
        return Result<ErpNextGlSummary>.Success(rows.Value!.Count == 0 ? new ErpNextGlSummary(0, 0, 0) : new ErpNextGlSummary((long)Dec(row, "n"), Dec(row, "debit"), Dec(row, "credit")));
    }

    public async Task<Result<ErpNextGlSummary>> GetGlOffsetSummaryAsync(ErpNextGlFilter filter, int skip, CancellationToken cancellationToken = default)
    {
        if (skip > MaxOffsetScan)
        {
            return Result<ErpNextGlSummary>.Failure(new Error("ErpNext.LedgerTooLarge", $"A statement page beyond line {MaxOffsetScan:N0} cannot be positioned; narrow the dates."));
        }

        var filters = await GlFiltersAsync(filter, cancellationToken);
        if (filters.IsFailure)
        {
            return Result<ErpNextGlSummary>.Failure(filters.Error);
        }

        long count = 0;
        decimal debit = 0, credit = 0;
        for (var start = 0; start < skip; start += OffsetChunk)
        {
            var take = Math.Min(OffsetChunk, skip - start);
            var rows = await QueryRowsAsync("GL Entry", ["debit", "credit"], filters.Value!, LedgerOrder, null, take, cancellationToken, start);
            if (rows.IsFailure)
            {
                return Result<ErpNextGlSummary>.Failure(rows.Error);
            }

            foreach (var row in rows.Value!)
            {
                debit += Dec(row, "debit");
                credit += Dec(row, "credit");
            }

            count += rows.Value!.Count;
            if (rows.Value!.Count < take)
            {
                break;
            }
        }

        return Result<ErpNextGlSummary>.Success(new ErpNextGlSummary(count, debit, credit));
    }

    /// <summary>The ledger filters of every line-level read: company, live entries, dates, and what the caller narrowed to.</summary>
    private async Task<Result<List<object[]>>> GlFiltersAsync(ErpNextGlFilter filter, CancellationToken cancellationToken)
    {
        var filters = await LedgerFiltersAsync(filter.From, filter.To, cancellationToken);
        if (filters.IsFailure)
        {
            return filters;
        }

        if (!string.IsNullOrWhiteSpace(filter.Account))
        {
            filters.Value!.Add(["account", "=", filter.Account]);
        }

        if (!string.IsNullOrWhiteSpace(filter.PartyType))
        {
            filters.Value!.Add(["party_type", "=", filter.PartyType]);
        }

        if (!string.IsNullOrWhiteSpace(filter.Party))
        {
            filters.Value!.Add(["party", "=", filter.Party]);
        }

        if (filter.VoucherNos is { Count: > 0 })
        {
            filters.Value!.Add(["voucher_no", "in", filter.VoucherNos.ToArray()]);
        }

        return filters;
    }

    public async Task<Result<IReadOnlyList<ErpNextPartyBalance>>> GetPartyBalancesAsync(string partyType, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var filters = await LedgerFiltersAsync(null, asOf, cancellationToken);
        if (filters.IsFailure)
        {
            return Result<IReadOnlyList<ErpNextPartyBalance>>.Failure(filters.Error);
        }

        filters.Value!.Add(["party_type", "=", partyType]);
        var rows = await QueryRowsAsync("GL Entry", ["party", Aggregate.Sum("debit"), Aggregate.Sum("credit")], filters.Value!, null, "party", 0, cancellationToken);
        return rows.IsFailure
            ? Result<IReadOnlyList<ErpNextPartyBalance>>.Failure(rows.Error)
            : Result<IReadOnlyList<ErpNextPartyBalance>>.Success(rows.Value!.Where(r => Text(r, "party") is not null).Select(r => new ErpNextPartyBalance(Text(r, "party")!, Dec(r, "debit"), Dec(r, "credit"))).ToList());
    }

    public async Task<Result<IReadOnlyList<ErpNextOpenInvoice>>> GetOpenInvoicesAsync(string doctype, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var partyField = doctype == "Sales Invoice" ? "customer" : doctype == "Purchase Invoice" ? "supplier" : null;
        if (partyField is null)
        {
            return Result<IReadOnlyList<ErpNextOpenInvoice>>.Failure(new Error("ErpNext.DoctypeNotAllowed", $"'{doctype}' has no outstanding amounts."));
        }

        var rows = await QueryRowsAsync(
            doctype,
            ["name", partyField, "posting_date", "due_date", "outstanding_amount"],
            [["docstatus", "=", 1], ["outstanding_amount", "!=", 0], ["posting_date", "<=", asOf.ToString("yyyy-MM-dd")]],
            "posting_date asc", null, 0, cancellationToken);
        return rows.IsFailure
            ? Result<IReadOnlyList<ErpNextOpenInvoice>>.Failure(rows.Error)
            : Result<IReadOnlyList<ErpNextOpenInvoice>>.Success(rows.Value!
                .Where(r => Text(r, "name") is not null && Text(r, partyField) is not null && Date(r, "posting_date") is not null)
                .Select(r => new ErpNextOpenInvoice(Text(r, "name")!, Text(r, partyField)!, Date(r, "posting_date")!.Value, Date(r, "due_date"), Dec(r, "outstanding_amount")))
                .ToList());
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>The filters every ledger read shares: this company, live (not cancelled) entries, an optional date window.</summary>
    private async Task<Result<List<object[]>>> LedgerFiltersAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var company = await GetCompanyAsync(cancellationToken);
        if (company.IsFailure)
        {
            return Result<List<object[]>>.Failure(company.Error);
        }

        var filters = new List<object[]> { new object[] { "company", "=", company.Value!.Name }, new object[] { "is_cancelled", "=", 0 } };
        if (from.HasValue)
        {
            filters.Add(["posting_date", ">=", from.Value.ToString("yyyy-MM-dd")]);
        }

        if (to.HasValue)
        {
            filters.Add(["posting_date", "<=", to.Value.ToString("yyyy-MM-dd")]);
        }

        return Result<List<object[]>>.Success(filters);
    }

    /// <summary>
    /// An aggregate column (SUM, COUNT) in a list query. Frappe changed how these are written: up to v15 only as SQL text
    /// ("sum(debit) as debit"), from v16 only as a dict ({"SUM": "debit", "as": "debit"}) — each version refuses the other's form.
    /// Queries carry this type and <see cref="QueryRowsAsync"/> writes whichever form the server accepts.
    /// </summary>
    internal sealed record Aggregate(string Function, string Field, string Alias)
    {
        public static Aggregate Sum(string field, string? alias = null) => new("SUM", field, alias ?? field);

        public static Aggregate Count(string field, string alias) => new("COUNT", field, alias);

        public JsonNode Render(bool dictSyntax) => dictSyntax
            ? new JsonObject { [Function] = Field, ["as"] = Alias }
            : JsonValue.Create($"{Function.ToLowerInvariant()}({Field}) as {Alias}");
    }

    /// <summary>
    /// Per ERPNext server: does it take aggregates in dict form (v16+)? Learned from the first aggregate query that succeeds and
    /// kept for the life of the process (a server upgrade is followed by a deploy, which restarts it).
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> AggregateDictSyntax = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A list query against Frappe's REST API. <paramref name="limit"/> 0 means "all rows". Fields are names or <see cref="Aggregate"/>s.</summary>
    private async Task<Result<List<JsonElement>>> QueryRowsAsync(
        string doctype, object[] fields, IReadOnlyList<object[]> filters, string? orderBy, string? groupBy, int limit, CancellationToken cancellationToken, int limitStart = 0)
    {
        if (!fields.OfType<Aggregate>().Any())
        {
            return await SendListAsync(doctype, fields, dictSyntax: false, filters, orderBy, groupBy, limit, limitStart, cancellationToken);
        }

        var server = _httpClient.BaseAddress?.ToString() ?? string.Empty;
        var known = AggregateDictSyntax.TryGetValue(server, out var dict);
        var preferred = !known || dict; // unknown server: try the current (v16) form first
        var first = await SendListAsync(doctype, fields, preferred, filters, orderBy, groupBy, limit, limitStart, cancellationToken);
        if (first.IsSuccess)
        {
            AggregateDictSyntax[server] = preferred;
            return first;
        }

        if (known || first.Error.Code == "ErpNext.AccessDenied")
        {
            return first;
        }

        // The server refused this form; it may be a version that only understands the other one.
        var second = await SendListAsync(doctype, fields, !preferred, filters, orderBy, groupBy, limit, limitStart, cancellationToken);
        if (second.IsSuccess)
        {
            _logger.LogInformation("ERPNext at {Server} takes aggregates in {Form} form.", server, preferred ? "text (Frappe ≤ v15)" : "dict (Frappe ≥ v16)");
            AggregateDictSyntax[server] = !preferred;
            return second;
        }

        return first;
    }

    private async Task<Result<List<JsonElement>>> SendListAsync(
        string doctype, object[] fields, bool dictSyntax, IReadOnlyList<object[]> filters, string? orderBy, string? groupBy, int limit, int limitStart, CancellationToken cancellationToken)
    {
        var fieldJson = new JsonArray(fields.Select(f => f is Aggregate a ? a.Render(dictSyntax) : JsonValue.Create((string)f)).ToArray<JsonNode?>());
        var url = $"api/resource/{Uri.EscapeDataString(doctype)}?fields={Uri.EscapeDataString(fieldJson.ToJsonString())}"
            + $"&filters={Uri.EscapeDataString(JsonSerializer.Serialize(filters))}&limit_page_length={limit.ToString(CultureInfo.InvariantCulture)}&limit_start={limitStart}";
        if (orderBy is not null)
        {
            url += $"&order_by={Uri.EscapeDataString(orderBy)}";
        }

        if (groupBy is not null)
        {
            url += $"&group_by={Uri.EscapeDataString(groupBy)}";
        }

        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var code = response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden ? "ErpNext.AccessDenied" : "ErpNext.ListFailed";
            return Result<List<JsonElement>>.Failure(new Error(code, $"{doctype}: {Explain(response.StatusCode, body)}"));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonSerializer.DeserializeAsync<JsonDocument>(stream, cancellationToken: cancellationToken);
        return Result<List<JsonElement>>.Success(document!.RootElement.GetProperty("data").EnumerateArray().Select(e => e.Clone()).ToList());
    }

    private static string? Text(JsonElement row, string key) =>
        row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString()) ? v.GetString() : null;

    private static decimal Dec(JsonElement row, string key) =>
        row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;

    private static DateOnly? Date(JsonElement row, string key) =>
        Text(row, key) is { Length: >= 10 } text && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}

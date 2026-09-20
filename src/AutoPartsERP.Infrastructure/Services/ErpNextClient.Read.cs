using System.Text.Json;

namespace AutoPartsERP.Infrastructure.Services;

/// <summary>Read side of the ERPNext hand-off: chart of accounts, account mapping and a read-only document browser.</summary>
public sealed partial class ErpNextClient
{
    /// <summary>Document types the browser may read, with the columns shown for each. Anything else is refused.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> BrowsableDocuments = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Sales Invoice"] = ["name", "customer", "posting_date", "due_date", "currency", "grand_total", "outstanding_amount", "status", "is_return"],
        ["Purchase Invoice"] = ["name", "supplier", "posting_date", "due_date", "currency", "grand_total", "outstanding_amount", "status", "is_return"],
        ["Payment Entry"] = ["name", "payment_type", "party_type", "party", "posting_date", "paid_amount", "mode_of_payment", "status"],
        ["Journal Entry"] = ["name", "voucher_type", "posting_date", "total_debit", "total_credit", "user_remark", "docstatus"],
        ["GL Entry"] = ["name", "posting_date", "account", "party", "debit", "credit", "voucher_type", "voucher_no", "remarks"],
        ["Customer"] = ["name", "customer_name", "customer_group", "territory", "disabled"],
        ["Supplier"] = ["name", "supplier_name", "supplier_group", "disabled"],
        ["Item"] = ["name", "item_name", "item_group", "stock_uom", "disabled"]
    };

    private const int MaxAccounts = 1000;
    private const int BalanceConcurrency = 8;

    public async Task<Result<IReadOnlyList<ErpNextAccount>>> GetChartOfAccountsAsync(bool includeBalances, CancellationToken cancellationToken = default)
    {
        var company = await GetCompanyAsync(cancellationToken);
        if (company.IsFailure)
        {
            return Result<IReadOnlyList<ErpNextAccount>>.Failure(company.Error);
        }

        var filters = Uri.EscapeDataString(JsonSerializer.Serialize(new object[]
        {
            new object[] { "company", "=", company.Value!.Name },
            new object[] { "disabled", "=", 0 }
        }));
        var fields = Uri.EscapeDataString("[\"name\",\"account_name\",\"parent_account\",\"is_group\",\"root_type\",\"account_type\",\"account_currency\"]");
        var response = await _httpClient.GetAsync(
            $"api/resource/Account?filters={filters}&fields={fields}&order_by=lft%20asc&limit_page_length={MaxAccounts}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Result<IReadOnlyList<ErpNextAccount>>.Failure(new Error("ErpNext.AccountsFailed", $"{response.StatusCode}: {Truncate(body)}"));
        }

        var accounts = new List<ErpNextAccount>();
        await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            var document = await JsonSerializer.DeserializeAsync<JsonDocument>(stream, cancellationToken: cancellationToken);
            foreach (var row in document!.RootElement.GetProperty("data").EnumerateArray())
            {
                string? Text(string key) => row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var name = Text("name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                accounts.Add(new ErpNextAccount(
                    name,
                    Text("account_name") ?? name,
                    Text("parent_account"),
                    row.TryGetProperty("is_group", out var g) && g.ValueKind is JsonValueKind.Number && g.GetInt32() == 1,
                    Text("root_type"),
                    Text("account_type"),
                    Text("account_currency"),
                    null));
            }
        }

        if (!includeBalances)
        {
            return Result<IReadOnlyList<ErpNextAccount>>.Success(accounts);
        }

        // ERPNext exposes no balance column on Account; get_balance_on is its own way to ask (groups include their children).
        var balances = new Dictionary<string, decimal?>(accounts.Count);
        using var gate = new SemaphoreSlim(BalanceConcurrency);
        await Task.WhenAll(accounts.Select(async account =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var value = await GetBalanceAsync(account.Name, cancellationToken);
                lock (balances) { balances[account.Name] = value; }
            }
            finally
            {
                gate.Release();
            }
        }));

        return Result<IReadOnlyList<ErpNextAccount>>.Success(accounts.Select(a => a with { Balance = balances.GetValueOrDefault(a.Name) }).ToList());
    }

    private async Task<decimal?> GetBalanceAsync(string account, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"api/method/erpnext.accounts.utils.get_balance_on?account={Uri.EscapeDataString(account)}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var document = await JsonSerializer.DeserializeAsync<JsonDocument>(stream, cancellationToken: cancellationToken);
            return document!.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetDecimal() : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    public async Task<Result<IReadOnlyList<ErpNextAccountMapping>>> GetAccountMappingAsync(CancellationToken cancellationToken = default)
    {
        var company = await GetCompanyAsync(cancellationToken);
        if (company.IsFailure)
        {
            return Result<IReadOnlyList<ErpNextAccountMapping>>.Failure(company.Error);
        }

        var c = company.Value!;
        var inventory = $"AutoPartsERP Inventory - {c.Abbr}";
        var inventoryExists = (await _httpClient.GetAsync($"api/resource/Account/{Uri.EscapeDataString(inventory)}", cancellationToken)).IsSuccessStatusCode;

        IReadOnlyList<ErpNextAccountMapping> map =
        [
            new("RECEIVABLE", "فواتير الزبائن وسندات القبض (فاتورة المبيعات مدين، سند القبض دائن)", c.ReceivableAccount),
            new("PAYABLE", "فواتير الموردين والدفعات لهم (فاتورة الشراء دائن، سند الدفع مدين)", c.PayableAccount),
            new("CASH", "القبض والدفع النقدي بالليرة وبالدولار", c.CashAccount),
            new("BANK", "القبض والدفع بالحوالة المصرفية والشيك", c.BankAccount),
            new("INCOME", "الإيراد الذي تُرحّله فواتير المبيعات", c.IncomeAccount),
            new("COGS", "تكلفة البضاعة المباعة، تُقيَّد مدينة عند ترحيل الفاتورة", c.CogsAccount),
            new("INVENTORY", "قيمة المخزون (المخزون يُدار في هذا النظام وأصناف ERPNext غير مخزنية)", inventoryExists ? inventory : null)
        ];
        return Result<IReadOnlyList<ErpNextAccountMapping>>.Success(map);
    }

    public async Task<Result<ErpNextDocumentPage>> ListDocumentsAsync(string doctype, int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        if (!BrowsableDocuments.TryGetValue(doctype, out var columns))
        {
            return Result<ErpNextDocumentPage>.Failure(new Error("ErpNext.DoctypeNotAllowed", $"'{doctype}' cannot be browsed."));
        }

        var size = Math.Clamp(pageSize, 1, 100);
        var start = (Math.Max(page, 1) - 1) * size;
        var filterList = new List<object>();
        if (!string.IsNullOrWhiteSpace(search))
        {
            filterList.Add(new object[] { "name", "like", $"%{search.Trim()}%" });
        }

        if (doctype == "GL Entry")
        {
            filterList.Add(new object[] { "is_cancelled", "=", 0 });
        }

        var filters = Uri.EscapeDataString(JsonSerializer.Serialize(filterList));
        var fields = Uri.EscapeDataString(JsonSerializer.Serialize(columns));
        var type = Uri.EscapeDataString(doctype);
        var response = await _httpClient.GetAsync(
            $"api/resource/{type}?fields={fields}&filters={filters}&order_by=modified%20desc&limit_start={start}&limit_page_length={size}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Result<ErpNextDocumentPage>.Failure(new Error("ErpNext.ListFailed", $"{response.StatusCode}: {Truncate(body)}"));
        }

        var rows = new List<IReadOnlyDictionary<string, JsonElement>>();
        await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            var document = await JsonSerializer.DeserializeAsync<JsonDocument>(stream, cancellationToken: cancellationToken);
            foreach (var row in document!.RootElement.GetProperty("data").EnumerateArray())
            {
                rows.Add(row.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()));
            }
        }

        long total = rows.Count;
        var countResponse = await _httpClient.GetAsync($"api/method/frappe.client.get_count?doctype={type}&filters={filters}", cancellationToken);
        if (countResponse.IsSuccessStatusCode)
        {
            await using var stream = await countResponse.Content.ReadAsStreamAsync(cancellationToken);
            var document = await JsonSerializer.DeserializeAsync<JsonDocument>(stream, cancellationToken: cancellationToken);
            if (document!.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Number)
            {
                total = m.GetInt64();
            }
        }

        return Result<ErpNextDocumentPage>.Success(new ErpNextDocumentPage(columns, rows, total));
    }
}

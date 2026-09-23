using System.Text.Json;

namespace AutoPartsERP.Infrastructure.Services;

/// <summary>Read side of the ERPNext hand-off: the chart of accounts and which account each application event posts to.</summary>
public sealed partial class ErpNextClient
{
    private const int MaxAccounts = 1000;

    public async Task<Result<IReadOnlyList<ErpNextAccount>>> GetChartOfAccountsAsync(CancellationToken cancellationToken = default)
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
            return Result<IReadOnlyList<ErpNextAccount>>.Failure(new Error("ErpNext.AccountsFailed", $"{Explain(response.StatusCode, body)}"));
        }

        var accounts = new List<ErpNextAccount>();
        await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            var document = await JsonSerializer.DeserializeAsync<JsonDocument>(stream, cancellationToken: cancellationToken);
            foreach (var row in document!.RootElement.GetProperty("data").EnumerateArray())
            {
                var name = Text(row, "name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                accounts.Add(new ErpNextAccount(
                    name,
                    Text(row, "account_name") ?? name,
                    Text(row, "parent_account"),
                    row.TryGetProperty("is_group", out var g) && g.ValueKind is JsonValueKind.Number && g.GetInt32() == 1,
                    Text(row, "root_type"),
                    Text(row, "account_type"),
                    Text(row, "account_currency")));
            }
        }

        return Result<IReadOnlyList<ErpNextAccount>>.Success(accounts);
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
}

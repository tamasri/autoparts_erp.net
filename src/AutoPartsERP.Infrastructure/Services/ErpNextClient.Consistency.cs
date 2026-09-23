using System.Globalization;
using System.Text.Json;

namespace AutoPartsERP.Infrastructure.Services;

/// <summary>
/// Read side for the consistency check and the reference lists. Only the doctypes and fields below can be read — this is deliberately
/// not a general document browser.
/// </summary>
public sealed partial class ErpNextClient
{
    private sealed record IndexSpec(string AmountField, bool IsTransaction);

    private static readonly IReadOnlyDictionary<string, IndexSpec> IndexSpecs = new Dictionary<string, IndexSpec>(StringComparer.Ordinal)
    {
        ["Customer"] = new("", false),
        ["Supplier"] = new("", false),
        ["Item"] = new("", false),
        ["Sales Invoice"] = new("grand_total", true),
        ["Purchase Invoice"] = new("grand_total", true),
        ["Payment Entry"] = new("paid_amount", true),
        ["Journal Entry"] = new("total_debit", true),
    };

    private sealed record ReferenceSpec(string Doctype, string[] Fields, string OrderBy, bool CompanyScoped);

    private static readonly IReadOnlyDictionary<string, ReferenceSpec> ReferenceSpecs = new Dictionary<string, ReferenceSpec>(StringComparer.Ordinal)
    {
        ["cost-centers"] = new("Cost Center", ["name", "cost_center_name", "parent_cost_center", "is_group", "disabled"], "lft asc", true),
        ["modes-of-payment"] = new("Mode of Payment", ["name", "type", "enabled"], "name asc", false),
        ["sales-taxes"] = new("Sales Taxes and Charges Template", ["name", "title", "is_default", "disabled"], "name asc", true),
        ["purchase-taxes"] = new("Purchase Taxes and Charges Template", ["name", "title", "is_default", "disabled"], "name asc", true),
        ["fiscal-years"] = new("Fiscal Year", ["name", "year_start_date", "year_end_date", "disabled"], "year_start_date desc", false),
        ["exchange-rates"] = new("Currency Exchange", ["date", "from_currency", "to_currency", "exchange_rate"], "date desc", false),
    };

    public async Task<Result<IReadOnlyList<ErpNextIndexRow>>> GetDocumentIndexAsync(string doctype, CancellationToken cancellationToken = default)
    {
        if (!IndexSpecs.TryGetValue(doctype, out var spec))
        {
            return Result<IReadOnlyList<ErpNextIndexRow>>.Failure(new Error("ErpNext.UnknownDoctype", $"'{doctype}' is not compared."));
        }

        var filters = new List<object[]>();
        var fields = new List<string> { "name" };
        if (spec.IsTransaction)
        {
            var company = await GetCompanyAsync(cancellationToken);
            if (company.IsFailure)
            {
                return Result<IReadOnlyList<ErpNextIndexRow>>.Failure(company.Error);
            }

            filters.Add(["company", "=", company.Value!.Name]);
            filters.Add(["docstatus", "in", new[] { 1, 2 }]);
            fields.Add("docstatus");
            fields.Add(spec.AmountField);
        }
        else
        {
            fields.Add("disabled");
        }

        var rows = await ListAsync(doctype, fields, filters, "name asc", cancellationToken);
        if (rows.IsFailure)
        {
            return Result<IReadOnlyList<ErpNextIndexRow>>.Failure(rows.Error);
        }

        return Result<IReadOnlyList<ErpNextIndexRow>>.Success(rows.Value!
            .Select(r => new ErpNextIndexRow(
                Text(r, "name") ?? string.Empty,
                spec.IsTransaction ? Number(r, spec.AmountField) : null,
                (int)(Number(r, "docstatus") ?? 0),
                (Number(r, "disabled") ?? 0) == 1,
                spec.IsTransaction))
            .Where(r => r.Name.Length > 0)
            .ToList());
    }

    public async Task<Result<ErpNextReferenceList>> GetReferenceListAsync(string kind, CancellationToken cancellationToken = default)
    {
        if (!ReferenceSpecs.TryGetValue(kind, out var spec))
        {
            return Result<ErpNextReferenceList>.Failure(new Error("ErpNext.UnknownList", $"'{kind}' is not an available list."));
        }

        var filters = new List<object[]>();
        if (spec.CompanyScoped)
        {
            var company = await GetCompanyAsync(cancellationToken);
            if (company.IsFailure)
            {
                return Result<ErpNextReferenceList>.Failure(company.Error);
            }

            filters.Add(["company", "=", company.Value!.Name]);
        }

        var rows = await ListAsync(spec.Doctype, spec.Fields, filters, spec.OrderBy, cancellationToken, limit: 500);
        if (rows.IsFailure)
        {
            return Result<ErpNextReferenceList>.Failure(rows.Error);
        }

        var values = rows.Value!
            .Select(r => (IReadOnlyDictionary<string, string?>)spec.Fields.ToDictionary(f => f, f => Scalar(r, f), StringComparer.Ordinal))
            .ToList();
        return Result<ErpNextReferenceList>.Success(new ErpNextReferenceList(spec.Doctype, spec.Fields, values));
    }

    /// <summary>GET /api/resource/&lt;doctype&gt; with the given fields and filters; <paramref name="limit"/> 0 = every row.</summary>
    private async Task<Result<IReadOnlyList<JsonElement>>> ListAsync(
        string doctype, IReadOnlyList<string> fields, IReadOnlyList<object[]> filters, string orderBy, CancellationToken cancellationToken, int limit = 0)
    {
        var query = $"fields={Uri.EscapeDataString(JsonSerializer.Serialize(fields))}"
            + (filters.Count > 0 ? $"&filters={Uri.EscapeDataString(JsonSerializer.Serialize(filters))}" : string.Empty)
            + $"&order_by={Uri.EscapeDataString(orderBy)}&limit_page_length={limit}";
        var response = await _httpClient.GetAsync($"api/resource/{Uri.EscapeDataString(doctype)}?{query}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Result<IReadOnlyList<JsonElement>>.Failure(new Error("ErpNext.ReadFailed", $"{doctype}: {response.StatusCode}: {Truncate(body)}"));
        }

        using var document = JsonDocument.Parse(body);
        return Result<IReadOnlyList<JsonElement>>.Success(document.RootElement.GetProperty("data").EnumerateArray().Select(e => e.Clone()).ToList());
    }

    private static decimal? Number(JsonElement row, string field) =>
        row.TryGetProperty(field, out var v) ? v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        } : null;

    private static string? Scalar(JsonElement row, string field) =>
        row.TryGetProperty(field, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetDecimal().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => "1",
            JsonValueKind.False => "0",
            _ => null
        } : null;
}

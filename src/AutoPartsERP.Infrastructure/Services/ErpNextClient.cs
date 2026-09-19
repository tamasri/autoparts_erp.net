using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace AutoPartsERP.Infrastructure.Services;

/// <summary>
/// Real implementation of the ERPNext accounting hand-off boundary, talking to Frappe's REST API
/// (/api/resource/&lt;Doctype&gt;) directly - no Frappe SDK exists for .NET.
///
/// Upsert strategy: POST to create; if Frappe reports the document already exists (409, or a
/// DuplicateEntryError in the body - Frappe is not fully consistent about which), fall back to PUT
/// against /api/resource/&lt;Doctype&gt;/&lt;name&gt;. Every failure is returned as a Result rather than
/// thrown, carrying Frappe's own error text, so InvoicePostedOutboxHandler (and any future caller)
/// can log the exact reason into erpnext_sync_log instead of a generic "sync failed".
///
/// Field defaults (item_group "All Item Groups", customer_group "Commercial", etc.) use
/// Frappe's standard root groups, which exist in every fresh install regardless of whether demo
/// data was seeded. If this specific instance's setup wizard produced different names, the first
/// sync attempt will surface Frappe's exact validation error in erpnext_sync_log rather than
/// failing silently - fix from that real error, not from a guess.
/// </summary>
public sealed class ErpNextClient : IErpNextClient
{
    private readonly HttpClient _httpClient;
    private readonly ErpNextOptions _options;
    private readonly ILogger<ErpNextClient> _logger;

    public ErpNextClient(HttpClient httpClient, IOptions<ErpNextOptions> options, ILogger<ErpNextClient> logger)
    {
        _options = options.Value;
        _logger = logger;

        httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("token", $"{_options.ApiKey}:{_options.ApiSecret}");
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient = httpClient;
    }

    public bool IsEnabled => _options.Enabled;

    public Task<Result<string>> SyncItemAsync(ErpNextItemSync item, CancellationToken cancellationToken = default) =>
        UpsertAsync(
            "Item",
            item.Code,
            new JsonObject
            {
                ["item_code"] = item.Code,
                ["item_name"] = item.NameEn,
                ["item_group"] = "All Item Groups",
                ["stock_uom"] = "Nos",
                ["is_stock_item"] = 1,
                ["valuation_rate"] = item.CostPrice,
                ["standard_rate"] = item.SellingPrice,
                ["description"] = item.NameAr
            },
            cancellationToken);

    public Task<Result<string>> SyncPartyAsync(ErpNextPartySync party, CancellationToken cancellationToken = default)
    {
        var isCustomer = string.Equals(party.PartyType, PartyTypeCodes.Customer, StringComparison.OrdinalIgnoreCase);
        var isVendor = string.Equals(party.PartyType, PartyTypeCodes.Vendor, StringComparison.OrdinalIgnoreCase);

        if (!isCustomer && !isVendor)
        {
            _logger.LogDebug("ERPNext sync skipped: party type {PartyType} has no ERPNext doctype mapping.", party.PartyType);
            return Task.FromResult(Result<string>.Success(string.Empty));
        }

        if (isCustomer)
        {
            return UpsertAsync(
                "Customer",
                party.Name,
                new JsonObject
                {
                    ["customer_name"] = party.Name,
                    ["customer_group"] = "Commercial",
                    ["territory"] = "Rest Of The World",
                    ["customer_type"] = "Individual",
                    ["tax_id"] = party.TaxId
                },
                cancellationToken);
        }

        return UpsertAsync(
            "Supplier",
            party.Name,
            new JsonObject
            {
                ["supplier_name"] = party.Name,
                ["supplier_group"] = "Local",
                ["supplier_type"] = "Individual",
                ["tax_id"] = party.TaxId
            },
            cancellationToken);
    }

    public async Task<Result<string>> SyncSalesInvoiceAsync(ErpNextSalesInvoiceSync invoice, CancellationToken cancellationToken = default)
    {
        if (invoice.Lines.Count == 0)
        {
            return Result<string>.Failure(new Error("ErpNext.NoLines", "Cannot sync a sales invoice with no lines to ERPNext."));
        }

        // NOTE: item_code must be the same code SyncItemAsync registered in ERPNext (our sku.Code),
        // not the local Guid - ErpNextInvoiceLineSync only carries ItemId today because
        // InvoicePostedOutboxHandler doesn't fetch invoice lines yet (see its own comment). Whoever
        // wires real line data through must resolve ItemId -> sku code before this reaches ERPNext,
        // or every invoice line will fail with "Item <guid> not found".
        var lines = new JsonArray();
        foreach (var line in invoice.Lines)
        {
            lines.Add(new JsonObject
            {
                ["item_code"] = line.ItemId.ToString(),
                ["qty"] = line.Quantity,
                ["rate"] = line.UnitPrice
            });
        }

        return await UpsertAsync(
            "Sales Invoice",
            invoice.InvoiceNumber,
            new JsonObject
            {
                ["customer"] = invoice.CustomerId.ToString(),
                ["posting_date"] = invoice.InvoiceDate.ToString("yyyy-MM-dd"),
                ["due_date"] = invoice.InvoiceDate.ToString("yyyy-MM-dd"),
                ["items"] = lines
            },
            cancellationToken);
    }

    public Task<Result<string>> SyncPaymentAsync(ErpNextPaymentSync payment, CancellationToken cancellationToken = default) =>
        UpsertAsync(
            "Payment Entry",
            payment.LocalPaymentId.ToString(),
            new JsonObject
            {
                ["payment_type"] = "Receive",
                ["party_type"] = "Customer",
                ["party"] = payment.CustomerId.ToString(),
                ["paid_amount"] = payment.Amount,
                ["received_amount"] = payment.Amount,
                ["posting_date"] = payment.PaymentDate.ToString("yyyy-MM-dd")
            },
            cancellationToken);

    private async Task<Result<string>> UpsertAsync(string doctype, string localName, JsonObject payload, CancellationToken cancellationToken)
    {
        var encodedType = Uri.EscapeDataString(doctype);
        var createResponse = await _httpClient.PostAsJsonAsync($"api/resource/{encodedType}", payload, cancellationToken);

        if (createResponse.IsSuccessStatusCode)
        {
            var name = await ExtractNameAsync(createResponse, cancellationToken);
            return Result<string>.Success(name ?? localName);
        }

        var body = await createResponse.Content.ReadAsStringAsync(cancellationToken);

        var looksLikeDuplicate = createResponse.StatusCode == HttpStatusCode.Conflict
            || body.Contains("DuplicateEntryError", StringComparison.OrdinalIgnoreCase);

        if (!looksLikeDuplicate)
        {
            _logger.LogWarning("ERPNext sync failed for {Doctype} {LocalName}: {Status} {Body}", doctype, localName, createResponse.StatusCode, body);
            return Result<string>.Failure(new Error("ErpNext.SyncFailed", $"{createResponse.StatusCode}: {Truncate(body)}"));
        }

        var updateResponse = await _httpClient.PutAsJsonAsync($"api/resource/{encodedType}/{Uri.EscapeDataString(localName)}", payload, cancellationToken);
        if (updateResponse.IsSuccessStatusCode)
        {
            var name = await ExtractNameAsync(updateResponse, cancellationToken);
            return Result<string>.Success(name ?? localName);
        }

        var updateBody = await updateResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("ERPNext update failed for {Doctype} {LocalName}: {Status} {Body}", doctype, localName, updateResponse.StatusCode, updateBody);
        return Result<string>.Failure(new Error("ErpNext.SyncFailed", $"{updateResponse.StatusCode}: {Truncate(updateBody)}"));
    }

    private static async Task<string?> ExtractNameAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var document = await JsonSerializer.DeserializeAsync<JsonDocument>(stream, cancellationToken: cancellationToken);
            return document?.RootElement.GetProperty("data").GetProperty("name").GetString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Truncate(string value) => value.Length > 500 ? value[..500] : value;
}

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
public sealed partial class ErpNextClient : IErpNextClient
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
                // This application owns inventory (quantities, locations, batches). Keeping items non-stock in ERPNext avoids a
                // second, competing stock ledger; cost of goods sold is booked by a Journal Entry instead (SyncCogsEntryAsync).
                ["is_stock_item"] = 0,
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
            return LinkPartyAsync("Customer", "customer_name", party, display => new JsonObject
            {
                ["customer_name"] = display,
                ["customer_group"] = "Commercial",
                ["territory"] = "Rest Of The World",
                ["customer_type"] = "Individual",
                ["tax_id"] = party.TaxId
            }, cancellationToken);
        }

        return LinkPartyAsync("Supplier", "supplier_name", party, display => new JsonObject
        {
            ["supplier_name"] = display,
            ["supplier_group"] = "Local",
            ["supplier_type"] = "Individual",
            ["tax_id"] = party.TaxId
        }, cancellationToken);
    }

    public async Task<Result<string>> SyncSalesInvoiceAsync(ErpNextSalesInvoiceSync invoice, CancellationToken cancellationToken = default)
    {
        if (invoice.Lines.Count == 0)
        {
            return Result<string>.Failure(new Error("ErpNext.NoLines", "Cannot sync a sales invoice with no lines to ERPNext."));
        }

        var lines = new JsonArray();
        foreach (var line in invoice.Lines)
        {
            lines.Add(new JsonObject
            {
                ["item_code"] = line.ItemCode,
                ["qty"] = invoice.IsReturn ? -line.Quantity : line.Quantity,
                ["rate"] = line.UnitPrice,
                ["discount_percentage"] = line.DiscountPercent
            });
        }

        // Our total = lines − invoice discount + delivery fee. ERPNext takes the discount off the net total and adds "Actual" charges after it,
        // so the fee goes in as a charge on the company's income account and both totals (and the receivable) agree.
        JsonArray? charges = null;
        if (invoice.DeliveryFee != 0)
        {
            var company = await GetCompanyAsync(cancellationToken);
            if (company.IsFailure)
            {
                return Result<string>.Failure(company.Error);
            }

            if (string.IsNullOrWhiteSpace(company.Value!.IncomeAccount))
            {
                return Result<string>.Failure(new Error("ErpNext.AccountsMissing", $"Company '{company.Value.Name}' has no default income account for the delivery fee."));
            }

            charges = new JsonArray
            {
                new JsonObject
                {
                    ["charge_type"] = "Actual",
                    ["account_head"] = company.Value.IncomeAccount,
                    ["description"] = "Delivery fee / أجور التوصيل",
                    ["tax_amount"] = invoice.IsReturn ? -invoice.DeliveryFee : invoice.DeliveryFee
                }
            };
        }

        // docstatus=1 inserts AND submits, which is what posts the general-ledger entries; a draft
        // Sales Invoice would sit in ERPNext without touching any account. update_stock stays 0
        // because this application, not ERPNext, is the system of record for inventory.
        var document = new JsonObject
        {
            ["customer"] = invoice.CustomerName,
            ["currency"] = _options.Currency,
            ["posting_date"] = invoice.InvoiceDate.ToString("yyyy-MM-dd"),
            ["set_posting_time"] = 1,
            ["due_date"] = invoice.DueDate.ToString("yyyy-MM-dd"),
            ["update_stock"] = 0,
            ["remarks"] = $"AutoPartsERP invoice {invoice.InvoiceNumber}",
            ["items"] = lines,
            ["is_return"] = invoice.IsReturn ? 1 : 0,
            ["docstatus"] = 1
        }.WithReturnAgainst(invoice.ReturnAgainst).WithInvoiceDiscount(invoice.DiscountAmount, invoice.IsReturn).WithSalesPerson(invoice.SalesPerson);
        if (charges is not null)
        {
            document["taxes"] = charges;
        }

        return await UpsertAsync(
            "Sales Invoice",
            invoice.InvoiceNumber,
            document,
            cancellationToken);
    }

    public Task<Result<string>> SyncSalesPersonAsync(ErpNextSalesPersonSync person, CancellationToken cancellationToken = default) =>
        UpsertAsync(
            "Sales Person",
            person.Name,
            new JsonObject
            {
                ["sales_person_name"] = person.Name,
                ["parent_sales_person"] = "Sales Team",
                ["is_group"] = 0,
                ["enabled"] = person.Enabled ? 1 : 0,
                ["commission_rate"] = person.CommissionRate
            },
            cancellationToken);

    public async Task<Result<string>> SyncPaymentAsync(ErpNextPaymentSync payment, CancellationToken cancellationToken = default)
    {
        var company = await GetCompanyAsync(cancellationToken);
        if (company.IsFailure)
        {
            return Result<string>.Failure(company.Error);
        }

        var c = company.Value!;
        var isCash = string.Equals(payment.PaymentMethod, "CASH", StringComparison.OrdinalIgnoreCase)
            || string.Equals(payment.PaymentMethod, "USD_CASH", StringComparison.OrdinalIgnoreCase);
        var paidTo = isCash ? c.CashAccount : (c.BankAccount ?? c.CashAccount);
        if (string.IsNullOrWhiteSpace(c.ReceivableAccount) || string.IsNullOrWhiteSpace(paidTo))
        {
            return Result<string>.Failure(new Error(
                "ErpNext.AccountsMissing",
                $"Company '{c.Name}' has no default receivable/{(isCash ? "cash" : "bank")} account in ERPNext; set it in the chart of accounts."));
        }

        // ERPNext refuses a reference larger than what it still considers outstanding (currency rounding, an earlier partial
        // payment). Clamp each allocation to the invoice's real outstanding; the remainder stays on the payment as an advance.
        var references = new JsonArray();
        foreach (var r in payment.References)
        {
            var outstanding = await GetOutstandingAsync("Sales Invoice", r.DocumentName, cancellationToken);
            var allocated = outstanding.HasValue ? Math.Min(r.AllocatedAmount, outstanding.Value) : r.AllocatedAmount;
            if (allocated <= 0)
            {
                continue;
            }

            references.Add(new JsonObject
            {
                ["reference_doctype"] = "Sales Invoice",
                ["reference_name"] = r.DocumentName,
                ["allocated_amount"] = allocated
            });
        }

        var date = payment.PaymentDate.ToString("yyyy-MM-dd");
        var doc = new JsonObject
        {
            ["payment_type"] = "Receive",
            ["company"] = c.Name,
            ["posting_date"] = date,
            ["party_type"] = "Customer",
            ["party"] = payment.CustomerName,
            ["paid_from"] = c.ReceivableAccount,
            ["paid_to"] = paidTo,
            ["paid_amount"] = payment.Amount,
            ["received_amount"] = payment.Amount,
            ["references"] = references,
            ["remarks"] = $"AutoPartsERP payment {payment.LocalPaymentId}",
            ["docstatus"] = 1
        };

        // ERPNext insists on a reference number/date for bank-type receipts.
        if (!isCash)
        {
            doc["reference_no"] = string.IsNullOrWhiteSpace(payment.ReferenceNumber) ? payment.LocalPaymentId.ToString("N")[..12] : payment.ReferenceNumber;
            doc["reference_date"] = date;
        }

        return await UpsertAsync("Payment Entry", payment.LocalPaymentId.ToString(), doc, cancellationToken);
    }

    public async Task<Result<string>> SyncCogsEntryAsync(ErpNextCogsEntrySync entry, CancellationToken cancellationToken = default)
    {
        var company = await GetCompanyAsync(cancellationToken);
        if (company.IsFailure)
        {
            return Result<string>.Failure(company.Error);
        }

        var c = company.Value!;
        if (string.IsNullOrWhiteSpace(c.CogsAccount))
        {
            return Result<string>.Failure(new Error("ErpNext.AccountsMissing", $"Company '{c.Name}' has no default Cost of Goods Sold account in ERPNext."));
        }

        var inventory = await EnsureInventoryAccountAsync(c, cancellationToken);
        if (inventory.IsFailure)
        {
            return Result<string>.Failure(inventory.Error);
        }

        // Sale: Dr COGS / Cr Inventory. Customer return: the reverse (goods come back at their cost).
        string debit = entry.IsReturn ? inventory.Value! : c.CogsAccount!;
        string credit = entry.IsReturn ? c.CogsAccount! : inventory.Value!;
        var doc = new JsonObject
        {
            ["voucher_type"] = "Journal Entry",
            ["company"] = c.Name,
            ["posting_date"] = entry.Date.ToString("yyyy-MM-dd"),
            ["user_remark"] = entry.Description ?? $"Cost of goods sold for AutoPartsERP invoice {entry.InvoiceNumber}",
            ["accounts"] = new JsonArray
            {
                new JsonObject { ["account"] = debit, ["debit_in_account_currency"] = entry.Amount },
                new JsonObject { ["account"] = credit, ["credit_in_account_currency"] = entry.Amount }
            },
            ["docstatus"] = 1
        };

        return await UpsertAsync("Journal Entry", entry.LocalInvoiceId.ToString(), doc, cancellationToken);
    }

    public async Task<Result<string>> SyncPurchaseInvoiceAsync(ErpNextPurchaseInvoiceSync bill, CancellationToken cancellationToken = default)
    {
        if (bill.Lines.Count == 0)
        {
            return Result<string>.Failure(new Error("ErpNext.NoLines", "Cannot sync a purchase invoice with no lines to ERPNext."));
        }

        var company = await GetCompanyAsync(cancellationToken);
        if (company.IsFailure)
        {
            return Result<string>.Failure(company.Error);
        }

        var inventory = await EnsureInventoryAccountAsync(company.Value!, cancellationToken);
        if (inventory.IsFailure)
        {
            return Result<string>.Failure(inventory.Error);
        }

        // Goods are received (and counted) in this application, so the bill debits the externally-managed inventory account
        // rather than creating stock in ERPNext (update_stock = 0).
        var items = new JsonArray();
        foreach (var line in bill.Lines)
        {
            items.Add(new JsonObject
            {
                ["item_code"] = line.ItemCode,
                ["qty"] = bill.IsReturn ? -line.Quantity : line.Quantity,
                ["rate"] = line.UnitPrice,
                ["discount_percentage"] = line.DiscountPercent,
                ["expense_account"] = inventory.Value
            });
        }

        var date = bill.BillDate.ToString("yyyy-MM-dd");
        return await UpsertAsync(
            "Purchase Invoice",
            bill.BillNumber,
            new JsonObject
            {
                ["supplier"] = bill.SupplierName,
                ["company"] = company.Value!.Name,
                ["currency"] = _options.Currency,
                ["posting_date"] = date,
                ["set_posting_time"] = 1,
                ["bill_no"] = bill.BillNumber,
                ["bill_date"] = date,
                ["due_date"] = bill.DueDate.ToString("yyyy-MM-dd"),
                ["update_stock"] = 0,
                ["is_return"] = bill.IsReturn ? 1 : 0,
                ["remarks"] = $"AutoPartsERP purchase invoice {bill.BillNumber}",
                ["items"] = items,
                ["docstatus"] = 1
            }.WithReturnAgainst(bill.ReturnAgainst).WithInvoiceDiscount(bill.DiscountAmount, bill.IsReturn),
            cancellationToken);
    }

    public async Task<Result<string>> SyncSupplierPaymentAsync(ErpNextSupplierPaymentSync payment, CancellationToken cancellationToken = default)
    {
        var company = await GetCompanyAsync(cancellationToken);
        if (company.IsFailure)
        {
            return Result<string>.Failure(company.Error);
        }

        var c = company.Value!;
        var isCash = string.Equals(payment.PaymentMethod, "CASH", StringComparison.OrdinalIgnoreCase)
            || string.Equals(payment.PaymentMethod, "USD_CASH", StringComparison.OrdinalIgnoreCase);
        var paidFrom = isCash ? c.CashAccount : (c.BankAccount ?? c.CashAccount);
        if (string.IsNullOrWhiteSpace(c.PayableAccount) || string.IsNullOrWhiteSpace(paidFrom))
        {
            return Result<string>.Failure(new Error("ErpNext.AccountsMissing", $"Company '{c.Name}' has no default payable/{(isCash ? "cash" : "bank")} account in ERPNext."));
        }

        var references = new JsonArray();
        foreach (var r in payment.References)
        {
            var outstanding = await GetOutstandingAsync("Purchase Invoice", r.DocumentName, cancellationToken);
            var allocated = outstanding.HasValue ? Math.Min(r.AllocatedAmount, outstanding.Value) : r.AllocatedAmount;
            if (allocated > 0)
            {
                references.Add(new JsonObject { ["reference_doctype"] = "Purchase Invoice", ["reference_name"] = r.DocumentName, ["allocated_amount"] = allocated });
            }
        }

        var date = payment.PaymentDate.ToString("yyyy-MM-dd");
        var doc = new JsonObject
        {
            ["payment_type"] = "Pay",
            ["company"] = c.Name,
            ["posting_date"] = date,
            ["party_type"] = "Supplier",
            ["party"] = payment.SupplierName,
            ["paid_from"] = paidFrom,
            ["paid_to"] = c.PayableAccount,
            ["paid_amount"] = payment.Amount,
            ["received_amount"] = payment.Amount,
            ["references"] = references,
            ["remarks"] = $"AutoPartsERP supplier payment {payment.LocalPaymentId}",
            ["docstatus"] = 1
        };
        if (!isCash)
        {
            doc["reference_no"] = string.IsNullOrWhiteSpace(payment.ReferenceNumber) ? payment.LocalPaymentId.ToString("N")[..12] : payment.ReferenceNumber;
            doc["reference_date"] = date;
        }

        return await UpsertAsync("Payment Entry", payment.LocalPaymentId.ToString(), doc, cancellationToken);
    }

    /// <summary>The account that carries inventory value while this application (not ERPNext) owns the stock; created on first use.</summary>
    private async Task<Result<string>> EnsureInventoryAccountAsync(CompanyInfo company, CancellationToken cancellationToken)
    {
        var name = $"AutoPartsERP Inventory - {company.Abbr}";
        var probe = await _httpClient.GetAsync($"api/resource/Account/{Uri.EscapeDataString(name)}", cancellationToken);
        if (probe.IsSuccessStatusCode)
        {
            return Result<string>.Success(name);
        }

        var created = await _httpClient.PostAsJsonAsync(
            "api/resource/Account",
            new JsonObject
            {
                ["account_name"] = "AutoPartsERP Inventory",
                ["company"] = company.Name,
                ["parent_account"] = await FindInventoryParentAccountAsync(company, cancellationToken),
                ["is_group"] = 0,
                ["root_type"] = "Asset",
                ["report_type"] = "Balance Sheet"
            },
            cancellationToken);

        if (created.IsSuccessStatusCode)
        {
            return Result<string>.Success(name);
        }

        var body = await created.Content.ReadAsStringAsync(cancellationToken);
        return Result<string>.Failure(new Error("ErpNext.AccountCreateFailed", $"Could not create the inventory account '{name}': {Explain(created.StatusCode, body)}"));
    }

    /// <summary>
    /// The group account under which the inventory account is created. ERPNext charts differ ("Current Assets - ABBR" is not
    /// guaranteed), so ask ERPNext for the company's asset groups and prefer Current Assets / Stock Assets.
    /// </summary>
    private async Task<string> FindInventoryParentAccountAsync(CompanyInfo company, CancellationToken cancellationToken)
    {
        var fallback = $"Current Assets - {company.Abbr}";
        try
        {
            var filters = Uri.EscapeDataString(JsonSerializer.Serialize(new object[]
            {
                new object[] { "company", "=", company.Name },
                new object[] { "is_group", "=", 1 },
                new object[] { "root_type", "=", "Asset" }
            }));
            var fields = Uri.EscapeDataString("[\"name\",\"account_name\",\"parent_account\"]");
            var response = await _httpClient.GetAsync($"api/resource/Account?filters={filters}&fields={fields}&limit_page_length=200", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return fallback;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var document = await JsonSerializer.DeserializeAsync<JsonDocument>(stream, cancellationToken: cancellationToken);
            var rows = document!.RootElement.GetProperty("data").EnumerateArray()
                .Select(e => (Name: e.GetProperty("name").GetString() ?? string.Empty,
                              Account: e.TryGetProperty("account_name", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null,
                              Parent: e.TryGetProperty("parent_account", out var pa) && pa.ValueKind == JsonValueKind.String ? pa.GetString() : null))
                .Where(r => r.Name.Length > 0)
                .ToList();

            return rows.FirstOrDefault(r => string.Equals(r.Account, "Current Assets", StringComparison.OrdinalIgnoreCase)).Name
                ?? rows.FirstOrDefault(r => string.Equals(r.Account, "Stock Assets", StringComparison.OrdinalIgnoreCase)).Name
                ?? rows.FirstOrDefault(r => !string.IsNullOrEmpty(r.Parent)).Name
                ?? rows.FirstOrDefault().Name
                ?? fallback;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list ERPNext asset groups; using the default parent account.");
            return fallback;
        }
    }

    /// <summary>What ERPNext still considers unpaid on a Sales/Purchase Invoice (null when it cannot be read).</summary>
    private async Task<decimal?> GetOutstandingAsync(string doctype, string name, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync($"api/resource/{Uri.EscapeDataString(doctype)}/{Uri.EscapeDataString(name)}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var document = await JsonSerializer.DeserializeAsync<JsonDocument>(stream, cancellationToken: cancellationToken);
            var data = document!.RootElement.GetProperty("data");
            return data.TryGetProperty("outstanding_amount", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<Result<string>> CancelDocumentAsync(string doctype, string name, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "api/method/frappe.client.cancel",
            new JsonObject { ["doctype"] = doctype, ["name"] = name },
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return Result<string>.Success(name);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("ERPNext cancel failed for {Doctype} {Name}: {Status} {Body}", doctype, name, response.StatusCode, body);
        return Result<string>.Failure(new Error("ErpNext.CancelFailed", $"{Explain(response.StatusCode, body)}"));
    }

    private sealed record CompanyInfo(string Name, string Abbr, string? ReceivableAccount, string? PayableAccount, string? CashAccount, string? BankAccount, string? CogsAccount, string? IncomeAccount = null);

    private CompanyInfo? _company;

    /// <summary>The single ERPNext company and its default accounts (receivable / cash / bank), read once per client instance.</summary>
    private async Task<Result<CompanyInfo>> GetCompanyAsync(CancellationToken cancellationToken)
    {
        if (_company is not null)
        {
            return Result<CompanyInfo>.Success(_company);
        }

        var fields = Uri.EscapeDataString("[\"name\",\"abbr\",\"default_receivable_account\",\"default_payable_account\",\"default_cash_account\",\"default_bank_account\",\"default_expense_account\",\"default_income_account\"]");
        var response = await _httpClient.GetAsync($"api/resource/Company?fields={fields}&limit_page_length=1", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Result<CompanyInfo>.Failure(new Error("ErpNext.CompanyLookupFailed", $"{Explain(response.StatusCode, body)}"));
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var document = await JsonSerializer.DeserializeAsync<JsonDocument>(stream, cancellationToken: cancellationToken);
            var first = document!.RootElement.GetProperty("data").EnumerateArray().First();
            string? Read(string key) => first.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            _company = new CompanyInfo(Read("name")!, Read("abbr") ?? string.Empty, Read("default_receivable_account"), Read("default_payable_account"), Read("default_cash_account"), Read("default_bank_account"), Read("default_expense_account"), Read("default_income_account"));
            return Result<CompanyInfo>.Success(_company);
        }
        catch (Exception ex)
        {
            return Result<CompanyInfo>.Failure(new Error("ErpNext.CompanyLookupFailed", $"Unexpected company response: {ex.Message}"));
        }
    }

    /// <summary>
    /// Create-or-update for customers and suppliers. ERPNext does not refuse a second party with the same name: it silently names
    /// it "X - 1", "X - 2", ... so the create-then-update-on-duplicate flow used for items would add a new record on every sync.
    /// The party is therefore looked up by its name first and updated in place when it exists.
    /// </summary>
    /// <summary>
    /// Links one local customer/supplier to exactly one ERPNext record and returns that record's name — the only value documents may
    /// use to refer to the party (display names are not unique here, and ERPNext may name records by series, e.g. CUST-00001).
    /// <list type="number">
    /// <item>Already linked (<see cref="ErpNextPartySync.KnownName"/>): update that record in place. A changed display name only changes
    /// its <c>customer_name</c>/<c>supplier_name</c>; the record keeps its name, so every invoice and payment stays attached.</item>
    /// <item>Not linked yet: adopt a record with this display name that no other local party owns (an earlier attempt whose answer was
    /// lost, or data entered in ERPNext first).</item>
    /// <item>Otherwise create one. If the display name is already used by another party's record, the new one is told apart by the
    /// local code: "Name (CODE)". Two local parties never share an ERPNext record, so their balances never merge.</item>
    /// </list>
    /// A failed lookup is a failure, never "not there" — guessing would create a duplicate.
    /// </summary>
    private async Task<Result<string>> LinkPartyAsync(string doctype, string nameField, ErpNextPartySync party, Func<string, JsonObject> payload, CancellationToken cancellationToken)
    {
        var resource = $"api/resource/{Uri.EscapeDataString(doctype)}";

        if (!string.IsNullOrWhiteSpace(party.KnownName))
        {
            var updated = await _httpClient.PutAsJsonAsync($"{resource}/{Uri.EscapeDataString(party.KnownName)}", payload(party.Name), cancellationToken);
            if (updated.IsSuccessStatusCode)
            {
                return Result<string>.Success(party.KnownName);
            }

            var body = await updated.Content.ReadAsStringAsync(cancellationToken);
            if (updated.StatusCode != HttpStatusCode.NotFound)
            {
                return Result<string>.Failure(new Error("ErpNext.SyncFailed", Explain(updated.StatusCode, body)));
            }

            _logger.LogWarning("ERPNext {Doctype} '{Name}' linked to party {PartyId} no longer exists; linking again.", doctype, party.KnownName, party.LocalPartyId);
        }

        var candidates = await FindNamesByFieldAsync(doctype, nameField, party.Name, cancellationToken);
        if (candidates.IsFailure)
        {
            return Result<string>.Failure(candidates.Error);
        }

        var taken = new HashSet<string>(party.TakenNames ?? [], StringComparer.Ordinal);
        if (candidates.Value!.FirstOrDefault(n => !taken.Contains(n)) is { } free)
        {
            var adopted = await _httpClient.PutAsJsonAsync($"{resource}/{Uri.EscapeDataString(free)}", payload(party.Name), cancellationToken);
            if (adopted.IsSuccessStatusCode)
            {
                return Result<string>.Success(free);
            }

            return Result<string>.Failure(new Error("ErpNext.SyncFailed", Explain(adopted.StatusCode, await adopted.Content.ReadAsStringAsync(cancellationToken))));
        }

        var display = candidates.Value!.Count > 0 && !string.IsNullOrWhiteSpace(party.Code) ? $"{party.Name} ({party.Code})" : party.Name;
        var created = await _httpClient.PostAsJsonAsync(resource, payload(display), cancellationToken);
        if (!created.IsSuccessStatusCode)
        {
            return Result<string>.Failure(new Error("ErpNext.SyncFailed", Explain(created.StatusCode, await created.Content.ReadAsStringAsync(cancellationToken))));
        }

        return await ExtractNameAsync(created, cancellationToken) is { Length: > 0 } name
            ? Result<string>.Success(name)
            : Result<string>.Failure(new Error("ErpNext.SyncFailed", $"ERPNext created the {doctype} but did not say its name."));
    }

    /// <summary>
    /// The ERPNext records whose <paramref name="field"/> equals <paramref name="value"/>, oldest first (empty when there are none);
    /// failure when the lookup itself fails, so the caller does not mistake "unreachable" for "not there".
    /// </summary>
    private async Task<Result<IReadOnlyList<string>>> FindNamesByFieldAsync(string doctype, string field, string value, CancellationToken cancellationToken)
    {
        try
        {
            var filters = Uri.EscapeDataString(JsonSerializer.Serialize(new object[] { new object[] { field, "=", value } }));
            var response = await _httpClient.GetAsync(
                $"api/resource/{Uri.EscapeDataString(doctype)}?filters={filters}&fields=%5B%22name%22%5D&order_by=creation%20asc&limit_page_length=20", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return Result<IReadOnlyList<string>>.Failure(new Error("ErpNext.LookupFailed", $"{doctype} lookup: {Explain(response.StatusCode, body)}"));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var document = await JsonSerializer.DeserializeAsync<JsonDocument>(stream, cancellationToken: cancellationToken);
            return Result<IReadOnlyList<string>>.Success(document!.RootElement.GetProperty("data").EnumerateArray()
                .Select(r => r.GetProperty("name").GetString()).OfType<string>().ToList());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or HttpRequestException or InvalidOperationException)
        {
            return Result<IReadOnlyList<string>>.Failure(new Error("ErpNext.LookupFailed", $"{doctype} lookup: {ex.Message}"));
        }
    }

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
            return Result<string>.Failure(new Error("ErpNext.SyncFailed", $"{Explain(createResponse.StatusCode, body)}"));
        }

        var updateResponse = await _httpClient.PutAsJsonAsync($"api/resource/{encodedType}/{Uri.EscapeDataString(localName)}", payload, cancellationToken);
        if (updateResponse.IsSuccessStatusCode)
        {
            var name = await ExtractNameAsync(updateResponse, cancellationToken);
            return Result<string>.Success(name ?? localName);
        }

        var updateBody = await updateResponse.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("ERPNext update failed for {Doctype} {LocalName}: {Status} {Body}", doctype, localName, updateResponse.StatusCode, updateBody);
        return Result<string>.Failure(new Error("ErpNext.SyncFailed", $"{Explain(updateResponse.StatusCode, updateBody)}"));
    }

    private static async Task<string?> ExtractNameAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var document = await JsonSerializer.DeserializeAsync<JsonDocument>(stream, cancellationToken: cancellationToken);
            return document?.RootElement.GetProperty("data").GetProperty("name").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The readable reason for a failed ERPNext call (stored in the sync log, shown on screens). The full response body — Frappe's
    /// traceback included — goes to the server log only.
    /// </summary>
    private string Explain(HttpStatusCode status, string body)
    {
        _logger.LogWarning("ERPNext answered {Status}: {Body}", (int)status, body.Length > 4000 ? body[..4000] : body);
        return ErpNextErrors.Describe(status, body);
    }
}

internal static class ErpNextJsonExtensions
{
    public static JsonObject WithReturnAgainst(this JsonObject doc, string? returnAgainst)
    {
        if (!string.IsNullOrWhiteSpace(returnAgainst))
        {
            doc["return_against"] = returnAgainst;
        }

        return doc;
    }

    /// <summary>Credits the whole invoice to one Sales Person (ERPNext requires the allocations to add up to 100 %).</summary>
    public static JsonObject WithSalesPerson(this JsonObject doc, string? salesPerson)
    {
        if (!string.IsNullOrWhiteSpace(salesPerson))
        {
            doc["sales_team"] = new JsonArray { new JsonObject { ["sales_person"] = salesPerson, ["allocated_percentage"] = 100 } };
        }

        return doc;
    }

    /// <summary>
    /// The discount on the whole invoice, taken off the net total the way ERPNext's own "Additional Discount" does
    /// (a return carries it negative, like its quantities). Nothing is added when there is no discount.
    /// </summary>
    public static JsonObject WithInvoiceDiscount(this JsonObject doc, decimal discountAmount, bool isReturn)
    {
        if (discountAmount != 0)
        {
            doc["apply_discount_on"] = "Net Total";
            doc["discount_amount"] = isReturn ? -discountAmount : discountAmount;
        }

        return doc;
    }
}

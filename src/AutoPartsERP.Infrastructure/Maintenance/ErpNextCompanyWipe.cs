using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AutoPartsERP.Infrastructure.Services;

namespace AutoPartsERP.Infrastructure.Maintenance;

/// <summary>
/// Empties the ERPNext company of trial data, using ERPNext's own tool for that:
/// <list type="number">
/// <item>A <c>Transaction Deletion Record</c> for the company (System Manager). ERPNext deletes every transaction of the company in the
/// background — invoices, payments, journal entries, GL and stock ledger — and keeps its setup: chart of accounts, cost centres,
/// warehouses, taxes, company. We wait until it reports Completed.</item>
/// <item>The masters that tool deliberately leaves: item prices, items, sales persons (not the group nodes), customers, suppliers.</item>
/// </list>
/// Works on Frappe v15 and v16 (v16 needs its "to delete" list generated before submitting; v15 has no such method).
/// </summary>
public sealed class ErpNextCompanyWipe
{
    private static readonly (string Doctype, object[][] Filters)[] Masters =
    [
        ("Item Price", []),
        ("Item", []),
        ("Sales Person", [["is_group", "=", 0]]),
        ("Customer", []),
        ("Supplier", []),
    ];

    private readonly HttpClient _http;
    private readonly Action<string> _log;

    public ErpNextCompanyWipe(ErpNextOptions options, Action<string> log, HttpMessageHandler? handler = null)
    {
        _log = log;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromMinutes(2);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", $"{options.ApiKey}:{options.ApiSecret}");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<bool> RunAsync(bool dryRun, TimeSpan deletionTimeout, CancellationToken ct)
    {
        var company = (await ListAsync("Company", [], ct)).FirstOrDefault();
        if (company is null)
        {
            _log("ERPNext: no company found — nothing to wipe.");
            return true;
        }

        _log($"ERPNext company: {company}");
        foreach (var dt in new[] { "Sales Invoice", "Purchase Invoice", "Payment Entry", "Journal Entry", "GL Entry" })
        {
            _log($"  {dt}: {(await ListAsync(dt, [["company", "=", company]], ct)).Count}");
        }

        foreach (var (doctype, filters) in Masters)
        {
            _log($"  {doctype}: {(await ListAsync(doctype, filters, ct)).Count}");
        }

        if (!await CanDeleteTransactionsAsync(ct))
        {
            return false;
        }

        if (dryRun)
        {
            return true;
        }

        if (!await DeleteTransactionsAsync(company, deletionTimeout, ct))
        {
            return false;
        }

        var ok = true;
        foreach (var (doctype, filters) in Masters)
        {
            ok &= await DeleteAllAsync(doctype, filters, ct);
        }

        return ok;
    }

    /// <summary>
    /// Checked before anything is deleted (and in the dry run, so nobody types the confirmation for a run that cannot finish): only
    /// System Manager may read or create a Transaction Deletion Record.
    /// </summary>
    private async Task<bool> CanDeleteTransactionsAsync(CancellationToken ct)
    {
        var r = await _http.GetAsync("api/resource/Transaction Deletion Record?fields=%5B%22name%22%5D&limit_page_length=1", ct);
        if (r.IsSuccessStatusCode)
        {
            return true;
        }

        var who = await _http.GetAsync("api/method/frappe.auth.get_logged_user", ct);
        var user = who.IsSuccessStatusCode
            ? JsonNode.Parse(await who.Content.ReadAsStringAsync(ct))?["message"]?.GetValue<string>() ?? "the API key's user"
            : "the API key's user";
        _log($"ERPNext: {user} may not use Transaction Deletion Record ({ErpNextErrors.Describe(r.StatusCode, await r.Content.ReadAsStringAsync(ct))}).");
        _log($"ERPNext: give {user} the System Manager role (ERPNext → User → {user} → Roles → System Manager → Save), then run again.");
        return false;
    }

    private async Task<bool> DeleteTransactionsAsync(string company, TimeSpan timeout, CancellationToken ct)
    {
        var created = await _http.PostAsJsonAsync("api/resource/Transaction Deletion Record", new JsonObject { ["company"] = company }, ct);
        var body = await created.Content.ReadAsStringAsync(ct);
        if (!created.IsSuccessStatusCode)
        {
            _log($"ERPNext: could not create the Transaction Deletion Record: {ErpNextErrors.Describe(created.StatusCode, body)}");
            return false;
        }

        var name = JsonNode.Parse(body)?["data"]?["name"]?.GetValue<string>() ?? throw new InvalidOperationException("No record name returned.");
        _log($"ERPNext: Transaction Deletion Record {name} created.");

        // v16: the list of doctypes to delete must exist before submitting (v15 builds it itself and has no such method).
        var listed = await _http.PostAsJsonAsync("api/method/run_doc_method",
            new JsonObject { ["dt"] = "Transaction Deletion Record", ["dn"] = name, ["method"] = "generate_to_delete_list" }, ct);
        _log(listed.IsSuccessStatusCode ? "ERPNext: to-delete list generated." : "ERPNext: no to-delete list method (older ERPNext) — continuing.");

        var doc = await GetDocAsync("Transaction Deletion Record", name, ct)
            ?? throw new InvalidOperationException($"Transaction Deletion Record {name} could not be read back.");
        var submitted = await _http.PostAsJsonAsync("api/method/frappe.client.submit", new JsonObject { ["doc"] = doc.DeepClone() }, ct);
        if (!submitted.IsSuccessStatusCode)
        {
            _log($"ERPNext: submitting {name} failed: {ErpNextErrors.Describe(submitted.StatusCode, await submitted.Content.ReadAsStringAsync(ct))}");
            return false;
        }

        _log("ERPNext: deletion started; waiting for it to finish (runs in ERPNext's background workers)…");
        var deadline = DateTime.UtcNow + timeout;
        var last = string.Empty;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            var status = (await GetDocAsync("Transaction Deletion Record", name, ct))?["status"]?.GetValue<string>() ?? string.Empty;
            if (status != last)
            {
                _log($"ERPNext: status {status}");
                last = status;
            }

            if (status == "Completed")
            {
                return true;
            }

            if (status is "Failed" or "Cancelled")
            {
                _log($"ERPNext: the deletion {status.ToLowerInvariant()} — open Transaction Deletion Record {name} in ERPNext for its error log.");
                return false;
            }
        }

        _log($"ERPNext: still not finished after {timeout.TotalMinutes:0} minutes (are ERPNext's background workers running?). Record: {name}.");
        return false;
    }

    private async Task<bool> DeleteAllAsync(string doctype, object[][] filters, CancellationToken ct)
    {
        var names = await ListAsync(doctype, filters, ct);
        int deleted = 0, failed = 0;
        foreach (var n in names)
        {
            var r = await _http.DeleteAsync($"api/resource/{Uri.EscapeDataString(doctype)}/{Uri.EscapeDataString(n)}", ct);
            if (r.IsSuccessStatusCode)
            {
                deleted++;
            }
            else
            {
                failed++;
                if (failed <= 5)
                {
                    _log($"  {doctype} {n}: {ErpNextErrors.Describe(r.StatusCode, await r.Content.ReadAsStringAsync(ct))}");
                }
            }
        }

        _log($"ERPNext: {doctype} — deleted {deleted}{(failed > 0 ? $", NOT deleted {failed}" : string.Empty)}.");
        return failed == 0;
    }

    private async Task<List<string>> ListAsync(string doctype, object[][] filters, CancellationToken ct)
    {
        var url = $"api/resource/{Uri.EscapeDataString(doctype)}?fields=%5B%22name%22%5D&limit_page_length=0"
            + $"&filters={Uri.EscapeDataString(JsonSerializer.Serialize(filters))}";
        var r = await _http.GetAsync(url, ct);
        var body = await r.Content.ReadAsStringAsync(ct);
        if (!r.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ERPNext list {doctype}: {ErpNextErrors.Describe(r.StatusCode, body)}");
        }

        return JsonNode.Parse(body)?["data"]?.AsArray().Select(x => x?["name"]?.GetValue<string>()).OfType<string>().ToList() ?? [];
    }

    private async Task<JsonNode?> GetDocAsync(string doctype, string name, CancellationToken ct)
    {
        var r = await _http.GetAsync($"api/resource/{Uri.EscapeDataString(doctype)}/{Uri.EscapeDataString(name)}", ct);
        return r.IsSuccessStatusCode ? JsonNode.Parse(await r.Content.ReadAsStringAsync(ct))?["data"] : null;
    }
}

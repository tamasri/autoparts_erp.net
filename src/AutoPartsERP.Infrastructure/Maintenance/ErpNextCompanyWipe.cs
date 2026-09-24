using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AutoPartsERP.Infrastructure.Services;

namespace AutoPartsERP.Infrastructure.Maintenance;

/// <summary>
/// Empties the ERPNext company of trial data, using ERPNext's own tool for that:
/// <list type="number">
/// <item>A <c>Transaction Deletion Record</c> for the company, requested the way ERPNext's Company → Delete Transactions does
/// (<c>create_transaction_deletion_request</c>, System Manager). ERPNext deletes every transaction of the company in the
/// background — invoices, payments, journal entries, GL and stock ledger — and keeps its setup: chart of accounts, cost centres,
/// warehouses, taxes, company. We wait until it reports Completed.</item>
/// <item>The masters that tool deliberately leaves: item prices, items, sales persons (not the group nodes), customers, suppliers.</item>
/// </list>
/// Works on ERPNext v15 and v16 (the method exists in both).
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
    /// System Manager may create a Transaction Deletion Record. Other roles may be allowed to read one, so the role itself is checked:
    /// a user's roles (Has Role rows under User) are readable by System Manager; refused means the role is missing.
    /// </summary>
    private async Task<bool> CanDeleteTransactionsAsync(CancellationToken ct)
    {
        var who = await _http.GetAsync("api/method/frappe.auth.get_logged_user", ct);
        var user = who.IsSuccessStatusCode
            ? JsonNode.Parse(await who.Content.ReadAsStringAsync(ct))?["message"]?.GetValue<string>() ?? "the API key's user"
            : "the API key's user";

        var filters = JsonSerializer.Serialize(new object[] { new object[] { "parent", "=", user }, new object[] { "parenttype", "=", "User" } });
        var r = await _http.GetAsync(
            "api/resource/Has Role?parent_doctype=User&fields=%5B%22role%22%5D&limit_page_length=0&filters=" + Uri.EscapeDataString(filters), ct);
        var body = await r.Content.ReadAsStringAsync(ct);
        var roles = r.IsSuccessStatusCode
            ? JsonNode.Parse(body)?["data"]?.AsArray().Select(x => x?["role"]?.GetValue<string>()).OfType<string>().ToList() ?? []
            : [];
        if (roles.Contains("System Manager"))
        {
            return true;
        }

        _log(r.IsSuccessStatusCode
            ? $"ERPNext: {user} does not have the System Manager role (roles: {string.Join(", ", roles)}), which the Transaction Deletion Record needs."
            : $"ERPNext: {user} may not read user roles, so it does not have the System Manager role ({ErpNextErrors.Describe(r.StatusCode, body)}).");
        _log($"ERPNext: sign in to ERPNext as Administrator → User → {user} → Roles → tick System Manager → Save, then run again.");
        return false;
    }

    private async Task<bool> DeleteTransactionsAsync(string company, TimeSpan timeout, CancellationToken ct)
    {
        // ERPNext's own "Delete Transactions" action (Company → Manage). System Manager has no create/submit right on the record itself
        // (v16), so creating it through the resource API is refused; this method checks System Manager and delete on the company, then
        // creates, lists (v16) and submits the record in one call. The record's name is the one that was not there before.
        var existing = await ListAsync("Transaction Deletion Record", [["company", "=", company]], ct);
        var requested = await _http.PostAsJsonAsync("api/method/erpnext.setup.doctype.company.company.create_transaction_deletion_request",
            new JsonObject { ["company"] = company }, ct);
        if (!requested.IsSuccessStatusCode)
        {
            _log($"ERPNext: the transaction deletion was refused: {ErpNextErrors.Describe(requested.StatusCode, await requested.Content.ReadAsStringAsync(ct))}");
            return false;
        }

        var name = (await ListAsync("Transaction Deletion Record", [["company", "=", company]], ct)).Except(existing).FirstOrDefault()
            ?? throw new InvalidOperationException("ERPNext accepted the deletion but no new Transaction Deletion Record was found.");

        _log($"ERPNext: deletion {name} started; waiting for it to finish (runs in ERPNext's background workers)…");
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

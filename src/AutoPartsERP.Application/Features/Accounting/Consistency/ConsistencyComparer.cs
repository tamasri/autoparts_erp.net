namespace AutoPartsERP.Application.Features.Accounting.Consistency;

/// <summary>A local record that should (or, once voided, should no longer) be active in ERPNext, with what the sync log knows about it.</summary>
/// <param name="Active">Posted / not voided / not disabled — expected to be live in ERPNext.</param>
/// <param name="SyncStatus">erpnext_sync_log status (SYNCED, CANCELLED, FAILED, SKIPPED) or null when it was never attempted.</param>
public sealed record LocalRecord(string EntityType, Guid Id, string Ref, decimal? Amount, bool Active, string? SyncStatus, string? ErpNextName, string? LastError);

/// <summary>An ERPNext document or master record. <c>Active</c> = submitted (docstatus 1) or not disabled; <c>Cancelled</c> = docstatus 2.</summary>
public sealed record RemoteRecord(string Name, decimal? Amount, bool Active, bool Cancelled);

/// <summary>
/// Compares one section. Pure: the handler gathers both sides, this decides what differs. Amounts are compared per record whenever both
/// sides have one (a local amount is left null when it cannot be re-derived exactly); totals are shown only when <paramref name="withTotals"/>.
/// </summary>
public static class ConsistencyComparer
{
    public const int MaxIssues = 200;
    private const decimal Tolerance = 0.01m;

    public static ErpNextConsistencySectionDto Compare(string key, string doctype, IReadOnlyList<LocalRecord> local, IReadOnlyList<RemoteRecord> remote, bool withTotals)
    {
        var remoteByName = remote.GroupBy(r => r.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var known = new HashSet<string>(StringComparer.Ordinal);
        var issues = new List<ErpNextConsistencyIssueDto>();
        var matched = 0;

        void Add(string kind, LocalRecord? l, RemoteRecord? r, string? detail) =>
            issues.Add(new ErpNextConsistencyIssueDto(kind, l?.EntityType, l?.Id, l?.Ref, r?.Name ?? l?.ErpNextName, l?.Amount, r?.Amount, detail));

        foreach (var l in local)
        {
            if (l.ErpNextName is { Length: > 0 } name && l.SyncStatus is "SYNCED" or "CANCELLED")
            {
                known.Add(name);
            }

            var sent = l.SyncStatus is "SYNCED" or "CANCELLED" && !string.IsNullOrEmpty(l.ErpNextName);
            if (!sent)
            {
                if (l.Active)
                {
                    Add("NOT_SENT", l, null, l.SyncStatus is null ? "لم يُرسل بعد" : $"{l.SyncStatus}: {l.LastError}");
                }

                continue;
            }

            if (!remoteByName.TryGetValue(l.ErpNextName!, out var r))
            {
                if (l.Active)
                {
                    Add("MISSING_IN_ERPNEXT", l, null, "أُرسل لكنه غير موجود في ERPNext");
                }

                continue;
            }

            if (l.Active && r.Cancelled)
            {
                Add("CANCELLED_IN_ERPNEXT_ONLY", l, r, "ملغى في ERPNext وفعّال هنا");
                continue;
            }

            if (!l.Active && r.Active)
            {
                Add("NOT_CANCELLED_IN_ERPNEXT", l, r, "ملغى هنا وما زال فعّالاً في ERPNext");
                continue;
            }

            if (!l.Active)
            {
                continue;
            }

            if (l.Amount is { } a && r.Amount is { } b && Math.Abs(a - b) > Tolerance)
            {
                Add("AMOUNT_DIFFERS", l, r, $"الفرق {a - b:0.##}");
                continue;
            }

            matched++;
        }

        foreach (var r in remote.Where(r => r.Active && !known.Contains(r.Name)))
        {
            Add("ONLY_IN_ERPNEXT", null, r, "موجود في ERPNext فقط");
        }

        var activeLocal = local.Where(l => l.Active).ToList();
        var activeRemote = remote.Where(r => r.Active).ToList();
        return new ErpNextConsistencySectionDto(
            key,
            doctype,
            activeLocal.Count,
            withTotals ? activeLocal.Sum(l => l.Amount ?? 0) : null,
            activeRemote.Count,
            withTotals ? activeRemote.Sum(r => r.Amount ?? 0) : null,
            matched,
            issues.GroupBy(i => i.Kind).ToDictionary(g => g.Key, g => g.Count()),
            issues.Take(MaxIssues).ToList(),
            null);
    }

    /// <summary>The section when ERPNext could not be read: the local side and the error, no comparison.</summary>
    public static ErpNextConsistencySectionDto Unreachable(string key, string doctype, IReadOnlyList<LocalRecord> local, bool withTotals, string error) =>
        new(key, doctype, local.Count(l => l.Active), withTotals ? local.Where(l => l.Active).Sum(l => l.Amount ?? 0) : null,
            0, null, 0, new Dictionary<string, int>(), [], error);
}

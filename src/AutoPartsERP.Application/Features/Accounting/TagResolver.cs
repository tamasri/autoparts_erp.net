using AutoPartsERP.Contracts.Accounting;

namespace AutoPartsERP.Application.Features.Accounting;

/// <summary>
/// Finds the tags on manual entries and on ledger vouchers. A manual entry can be tagged from its own list (by id) or from the ledger
/// (by its ERPNext voucher number); both are the same entry once it has been booked, so lookups see the union of the two.
/// </summary>
internal static class TagResolver
{
    public const string JournalEntryTarget = "JOURNAL_ENTRY";
    public const string ErpNextTarget = "ERPNEXT";
    public const string JournalDoctype = "Journal Entry";

    public static string VoucherKey(string voucherType, string voucherNo) => $"{voucherType}|{voucherNo}";

    /// <summary>Tags per voucher, keyed by <see cref="VoucherKey"/>.</summary>
    public static async Task<Dictionary<string, List<TagDto>>> ForVouchersAsync(DbConnection connection, IEnumerable<(string Type, string No)> vouchers, CancellationToken cancellationToken)
    {
        var keys = vouchers.Select(v => VoucherKey(v.Type, v.No)).Distinct(StringComparer.Ordinal).ToArray();
        var result = new Dictionary<string, List<TagDto>>(StringComparer.Ordinal);
        if (keys.Length == 0)
        {
            return result;
        }

        var journalNames = keys.Where(k => k.StartsWith(JournalDoctype + "|", StringComparison.Ordinal)).Select(k => k[(JournalDoctype.Length + 1)..]).ToArray();
        var localByName = (await connection.QueryAsync<(string Name, Guid Id)>(new CommandDefinition(
            """
            SELECT erpnext_name AS Name, local_entity_id AS Id FROM erpnext_sync_log
            WHERE local_entity_type = 'JournalEntry' AND erpnext_doctype = 'Journal Entry' AND erpnext_name = ANY(@journalNames);
            """,
            new { journalNames }, cancellationToken: cancellationToken))).ToDictionary(x => x.Name, x => x.Id, StringComparer.Ordinal);

        var links = await LoadAsync(connection, keys, localByName.Values.Select(v => v.ToString()).ToArray(), cancellationToken);
        var nameByLocal = localByName.ToDictionary(kv => kv.Value.ToString(), kv => VoucherKey(JournalDoctype, kv.Key), StringComparer.Ordinal);
        foreach (var link in links)
        {
            var key = link.TargetType == ErpNextTarget ? link.TargetKey : nameByLocal.GetValueOrDefault(link.TargetKey);
            if (key is not null) Add(result, key, link.Tag);
        }

        return result;
    }

    /// <summary>Tags per manual entry id.</summary>
    public static async Task<Dictionary<Guid, List<TagDto>>> ForEntriesAsync(DbConnection connection, IEnumerable<Guid> entryIds, CancellationToken cancellationToken)
    {
        var ids = entryIds.Distinct().ToArray();
        var result = new Dictionary<Guid, List<TagDto>>();
        if (ids.Length == 0)
        {
            return result;
        }

        var nameById = (await connection.QueryAsync<(Guid Id, string Name)>(new CommandDefinition(
            """
            SELECT local_entity_id AS Id, erpnext_name AS Name FROM erpnext_sync_log
            WHERE local_entity_type = 'JournalEntry' AND erpnext_doctype = 'Journal Entry' AND erpnext_name IS NOT NULL AND local_entity_id = ANY(@ids);
            """,
            new { ids }, cancellationToken: cancellationToken))).ToDictionary(x => VoucherKey(JournalDoctype, x.Name), x => x.Id, StringComparer.Ordinal);

        var links = await LoadAsync(connection, nameById.Keys.ToArray(), ids.Select(i => i.ToString()).ToArray(), cancellationToken);
        foreach (var link in links)
        {
            Guid? id = link.TargetType == JournalEntryTarget ? Guid.Parse(link.TargetKey) : nameById.GetValueOrDefault(link.TargetKey);
            if (id is not null)
            {
                if (!result.TryGetValue(id.Value, out var list)) result[id.Value] = list = [];
                if (list.All(t => t.Id != link.Tag.Id)) list.Add(link.Tag);
            }
        }

        return result;
    }

    /// <summary>The ERPNext voucher numbers carrying a tag, whether it was put on the ERPNext voucher or on the manual entry that was booked as it.</summary>
    public static async Task<IReadOnlyList<string>> VouchersForTagAsync(DbConnection connection, Guid tagId, CancellationToken cancellationToken) =>
        (await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT v FROM (
                SELECT split_part(l.target_key, '|', 2) AS v
                FROM accounting_tag_links l WHERE l.tag_id = @tagId AND l.target_type = 'ERPNEXT'
                UNION
                SELECT s.erpnext_name AS v
                FROM accounting_tag_links l
                INNER JOIN erpnext_sync_log s ON s.local_entity_type = 'JournalEntry' AND s.local_entity_id::text = l.target_key AND s.erpnext_doctype = 'Journal Entry'
                WHERE l.tag_id = @tagId AND l.target_type = 'JOURNAL_ENTRY' AND s.erpnext_name IS NOT NULL
            ) t WHERE v <> '' ORDER BY v;
            """,
            new { tagId }, cancellationToken: cancellationToken))).ToList();

    private static async Task<List<TagLink>> LoadAsync(DbConnection connection, string[] erpKeys, string[] entryKeys, CancellationToken cancellationToken) =>
        (await connection.QueryAsync<TagLink>(new CommandDefinition(
            """
            SELECT l.target_type AS TargetType, l.target_key AS TargetKey, t.id AS Id, t.name AS Name, t.color AS Color
            FROM accounting_tag_links l INNER JOIN accounting_tags t ON t.id = l.tag_id
            WHERE (l.target_type = 'ERPNEXT' AND l.target_key = ANY(@erpKeys)) OR (l.target_type = 'JOURNAL_ENTRY' AND l.target_key = ANY(@entryKeys))
            ORDER BY t.name;
            """,
            new { erpKeys, entryKeys }, cancellationToken: cancellationToken))).ToList();

    private static void Add(Dictionary<string, List<TagDto>> map, string key, TagDto tag)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        if (list.All(t => t.Id != tag.Id)) list.Add(tag);
    }

    private sealed record TagLink(string TargetType, string TargetKey, Guid Id, string Name, string Color)
    {
        public TagDto Tag => new(Id, Name, Color);
    }
}

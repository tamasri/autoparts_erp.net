namespace AutoPartsERP.Infrastructure.Services;

/// <summary>The one place that records the outcome of an ERPNext hand-off in erpnext_sync_log (upsert per local entity + doctype).</summary>
internal static class ErpNextSyncLogWriter
{
    public const string Synced = "SYNCED";
    public const string Failed = "FAILED";
    public const string Skipped = "SKIPPED";
    public const string Cancelled = "CANCELLED";

    public static Task WriteAsync(
        DbConnection connection,
        string localEntityType,
        Guid localEntityId,
        string doctype,
        string? erpNextName,
        string status,
        string? lastError,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO erpnext_sync_log (id, local_entity_type, local_entity_id, erpnext_doctype, erpnext_name, status, last_error, attempt_count, synced_at, created_at, updated_at)
            VALUES (uuid_generate_v4(), @LocalEntityType, @LocalEntityId, @Doctype, @ErpNextName, @Status, @LastError, 1, @SyncedAt, now(), now())
            ON CONFLICT (local_entity_type, local_entity_id, erpnext_doctype) DO UPDATE
                SET erpnext_name = COALESCE(EXCLUDED.erpnext_name, erpnext_sync_log.erpnext_name),
                    status = EXCLUDED.status,
                    last_error = EXCLUDED.last_error,
                    attempt_count = erpnext_sync_log.attempt_count + 1,
                    synced_at = COALESCE(EXCLUDED.synced_at, erpnext_sync_log.synced_at),
                    updated_at = now();
            """,
            new
            {
                LocalEntityType = localEntityType,
                LocalEntityId = localEntityId,
                Doctype = doctype,
                ErpNextName = string.IsNullOrEmpty(erpNextName) ? null : erpNextName,
                Status = status,
                LastError = lastError,
                SyncedAt = status is Synced or Cancelled ? DateTimeOffset.UtcNow : (DateTimeOffset?)null
            },
            cancellationToken: cancellationToken));

    /// <summary>ERPNext name recorded for an entity that reached the SYNCED state, or null.</summary>
    public static Task<string?> FindSyncedNameAsync(
        DbConnection connection, string localEntityType, Guid localEntityId, string doctype, CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT erpnext_name FROM erpnext_sync_log
            WHERE local_entity_type = @LocalEntityType AND local_entity_id = @LocalEntityId
              AND erpnext_doctype = @Doctype AND status = 'SYNCED';
            """,
            new { LocalEntityType = localEntityType, LocalEntityId = localEntityId, Doctype = doctype },
            cancellationToken: cancellationToken));
}

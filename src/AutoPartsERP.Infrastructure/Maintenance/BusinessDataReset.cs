namespace AutoPartsERP.Infrastructure.Maintenance;

/// <summary>
/// Removes all business data from the application database, keeping the installation itself: users with the SYSTEM_ADMIN role
/// (optionally every user), roles and permissions, reason codes, KPI definitions, item categories, accounting entry types, AI
/// feature flags and the reference tables that migrations fill. Numbering restarts at 1. Meant for the moment trial data is
/// thrown away before real work starts; nothing here can be undone.
///
/// Every table in <c>public</c> must be listed as either wiped or kept: a table added by a later migration makes the reset stop
/// until someone decides which list it belongs to.
/// </summary>
public static class BusinessDataReset
{
    public static readonly IReadOnlyList<string> Wiped =
    [
        "accounting_tag_links", "accounting_tags", "ai_documents", "ai_feedback", "ai_prompt_logs", "ai_sessions", "ai_suggestions", "ai_task_runs",
        "approval_decisions", "approval_requests", "assistant_links", "audit_logs", "barcode_scan_logs", "batch_movements", "batches", "customers",
        "cycle_count_lines", "cycle_count_plans", "deleted_documents", "document_series", "erpnext_sync_log", "fx_rates", "idempotency_keys", "inventory_alerts", "inventory_balances",
        "inventory_movements", "inventory_stock", "invoice_lines", "invoices", "issue_order_lines", "issue_orders", "item_aliases",
        "item_interchanges", "item_reorder_settings", "items", "journal_entries", "journal_entry_lines", "kpi_thresholds",
        "ledger_reconciliation_items", "ledger_reconciliations", "locations", "outbox_messages", "parties", "party_addresses", "party_contacts",
        "party_notes", "party_type_assignments", "payment_allocations", "payments", "period_locks", "pick_tasks", "purchase_invoice_lines",
        "purchase_invoices", "putaway_tasks", "receiving_documents", "receiving_lines", "rejection_attempts", "sales_reps", "skus",
        "stock_adjustment_lines", "stock_adjustments", "supplier_payment_allocations", "supplier_payments", "transfer_order_lines",
        "transfer_orders", "transfer_request_lines", "transfer_requests", "user_warehouses", "warranty_records",
    ];

    public static readonly IReadOnlyList<string> Kept =
    [
        "__EFMigrationsHistory", "asp_net_role_claims", "asp_net_roles", "asp_net_user_claims", "asp_net_user_logins", "asp_net_user_roles",
        "asp_net_user_tokens", "asp_net_users", "ai_feature_flags", "ai_scheduled_tasks", "attribute_schemas", "categories", "entry_types",
        "inventory_statuses", "kpi_definitions", "party_type_catalog", "reason_codes",
    ];

    /// <summary>Codes that are not documents (parties, warranties) restart at 1 too. Documents are numbered by document_series (reseeded below).</summary>
    private static readonly string[] NumberSequences = ["warranty_number_seq", "party_code_seq"];

    public static async Task<bool> RunAsync(string connectionString, bool dryRun, bool keepAllUsers, Action<string> log, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var tables = (await connection.QueryAsync<string>(
            "SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename;")).ToList();
        var unknown = tables.Except(Wiped).Except(Kept).ToList();
        if (unknown.Count > 0)
        {
            log($"STOPPED: these tables are in neither list (add them to BusinessDataReset.Wiped or .Kept first): {string.Join(", ", unknown)}");
            return false;
        }

        var present = Wiped.Where(tables.Contains).ToList();
        long rows = 0;
        foreach (var t in present)
        {
            rows += await connection.ExecuteScalarAsync<long>($"SELECT count(*) FROM \"{t}\";");
        }

        var removedUsers = (await connection.QueryAsync<string>(
            """
            SELECT u.user_name FROM asp_net_users u
            WHERE NOT EXISTS (SELECT 1 FROM asp_net_user_roles ur INNER JOIN asp_net_roles r ON r.id = ur.role_id
                              WHERE ur.user_id = u.id AND r.name = 'SYSTEM_ADMIN')
            ORDER BY u.user_name;
            """)).ToList();

        log($"Application database: {present.Count} business tables, {rows:N0} rows to delete.");
        log(keepAllUsers
            ? "Users: all kept."
            : $"Users: SYSTEM_ADMIN accounts kept; {removedUsers.Count} other account(s) to remove{(removedUsers.Count > 0 ? ": " + string.Join(", ", removedUsers.Take(20)) + (removedUsers.Count > 20 ? ", …" : string.Empty) : string.Empty)}.");
        if (dryRun)
        {
            return true;
        }

        await using var tx = await connection.BeginTransactionAsync(ct);

        // One statement for all of them: tables that reference each other are emptied together, no CASCADE into kept tables.
        await connection.ExecuteAsync($"TRUNCATE {string.Join(", ", present.Select(t => $"\"{t}\""))} RESTART IDENTITY;", transaction: tx);

        foreach (var seq in NumberSequences)
        {
            await connection.ExecuteAsync($"ALTER SEQUENCE IF EXISTS \"{seq}\" RESTART WITH 1;", transaction: tx);
        }

        // Entry types: keep the built-in ones, drop the ones added while trying the system; then every document series starts again at 1.
        await connection.ExecuteAsync("DELETE FROM entry_types WHERE NOT is_system; SELECT seed_document_series();", transaction: tx);

        if (!keepAllUsers)
        {
            await connection.ExecuteAsync(
                """
                CREATE TEMP TABLE reset_users ON COMMIT DROP AS
                SELECT u.id FROM asp_net_users u
                WHERE NOT EXISTS (SELECT 1 FROM asp_net_user_roles ur INNER JOIN asp_net_roles r ON r.id = ur.role_id
                                  WHERE ur.user_id = u.id AND r.name = 'SYSTEM_ADMIN');
                DELETE FROM asp_net_user_claims WHERE user_id IN (SELECT id FROM reset_users);
                DELETE FROM asp_net_user_logins WHERE user_id IN (SELECT id FROM reset_users);
                DELETE FROM asp_net_user_tokens WHERE user_id IN (SELECT id FROM reset_users);
                DELETE FROM asp_net_user_roles WHERE user_id IN (SELECT id FROM reset_users);
                DELETE FROM asp_net_users WHERE id IN (SELECT id FROM reset_users);
                """, transaction: tx);
        }

        await tx.CommitAsync(ct);
        log("Application database: business data removed, numbering restarted.");
        return true;
    }
}

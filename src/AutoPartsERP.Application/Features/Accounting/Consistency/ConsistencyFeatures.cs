namespace AutoPartsERP.Application.Features.Accounting.Consistency;

/// <summary>
/// The consistency check: for each kind of record, what this application expects ERPNext to hold (read with the sync log, which links
/// every local record to its ERPNext name) against what ERPNext really holds. Read-only; re-sending is the sync job's job.
/// </summary>
public sealed record GetErpNextConsistencyQuery : IRequest<Result<ErpNextConsistencyDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetErpNextConsistencyQueryHandler : IRequestHandler<GetErpNextConsistencyQuery, Result<ErpNextConsistencyDto>>
{
    private sealed record Section(string Key, string Doctype, bool WithTotals, string Sql);

    // Every query returns LocalRecord columns. "Active" = expected to be live in ERPNext now.
    private static readonly Section[] Sections =
    [
        new("customers", "Customer", false, Party("CUSTOMER", "Customer")),
        new("suppliers", "Supplier", false, Party("VENDOR", "Supplier")),
        new("items", "Item", false,
            """
            SELECT 'Sku' AS EntityType, s.id AS Id, s.code AS Ref, NULL::numeric AS Amount, s.is_active AS Active,
                   l.status AS SyncStatus, l.erpnext_name AS ErpNextName, l.last_error AS LastError
            FROM skus s
            LEFT JOIN erpnext_sync_log l ON l.local_entity_type = 'Sku' AND l.local_entity_id = s.id AND l.erpnext_doctype = 'Item';
            """),
        new("sales-invoices", "Sales Invoice", true,
            """
            SELECT 'Invoice' AS EntityType, i.id AS Id, COALESCE(i.invoice_number, i.id::text) AS Ref, i.total_usd AS Amount, i.status = 'POSTED' AS Active,
                   l.status AS SyncStatus, l.erpnext_name AS ErpNextName, l.last_error AS LastError
            FROM invoices i
            LEFT JOIN erpnext_sync_log l ON l.local_entity_type = 'Invoice' AND l.local_entity_id = i.id AND l.erpnext_doctype = 'Sales Invoice'
            WHERE i.invoice_type IN ('SALE', 'RETURN') AND i.status IN ('POSTED', 'VOID') AND i.posted_at IS NOT NULL;
            """),
        new("purchase-invoices", "Purchase Invoice", true,
            """
            SELECT 'PurchaseInvoice' AS EntityType, p.id AS Id, p.bill_number AS Ref, p.total_usd AS Amount, p.status = 'POSTED' AS Active,
                   l.status AS SyncStatus, l.erpnext_name AS ErpNextName, l.last_error AS LastError
            FROM purchase_invoices p
            LEFT JOIN erpnext_sync_log l ON l.local_entity_type = 'PurchaseInvoice' AND l.local_entity_id = p.id AND l.erpnext_doctype = 'Purchase Invoice'
            WHERE p.status IN ('POSTED', 'VOID') AND p.posted_at IS NOT NULL;
            """),
        new("payments", "Payment Entry", true,
            """
            SELECT 'Payment' AS EntityType, p.id AS Id, p.payment_number AS Ref,
                   CASE WHEN p.amount_usd > 0 THEN p.amount_usd WHEN f.mid_rate > 0 THEN round(p.amount_syp / f.mid_rate, 4) ELSE 0 END AS Amount,
                   NOT p.is_reversed AS Active, l.status AS SyncStatus, l.erpnext_name AS ErpNextName, l.last_error AS LastError
            FROM payments p
            LEFT JOIN fx_rates f ON f.id = p.fx_rate_id
            LEFT JOIN erpnext_sync_log l ON l.local_entity_type = 'Payment' AND l.local_entity_id = p.id AND l.erpnext_doctype = 'Payment Entry'
            WHERE p.payment_type = 'RECEIPT'
            UNION ALL
            SELECT 'SupplierPayment', s.id, s.payment_number, s.amount_usd, NOT s.is_reversed, l.status, l.erpnext_name, l.last_error
            FROM supplier_payments s
            LEFT JOIN erpnext_sync_log l ON l.local_entity_type = 'SupplierPayment' AND l.local_entity_id = s.id AND l.erpnext_doctype = 'Payment Entry';
            """),
        // Manual entries, the cost-of-goods entry of each invoice, the entry of each landed cost voucher (its debits = its charges) and the
        // entry of each stock adjustment. An adjustment is valued at the item cost
        // when it was sent, which cannot be re-derived, so its amount is not compared; one skipped for having no value is not expected in ERPNext.
        new("journal-entries", "Journal Entry", false,
            """
            SELECT 'JournalEntry' AS EntityType, e.id AS Id, e.entry_number AS Ref,
                   (SELECT COALESCE(sum(x.debit_usd), 0) FROM journal_entry_lines x WHERE x.journal_entry_id = e.id) AS Amount,
                   e.status = 'POSTED' AS Active, l.status AS SyncStatus, l.erpnext_name AS ErpNextName, l.last_error AS LastError
            FROM journal_entries e
            LEFT JOIN erpnext_sync_log l ON l.local_entity_type = 'JournalEntry' AND l.local_entity_id = e.id AND l.erpnext_doctype = 'Journal Entry'
            WHERE e.status IN ('POSTED', 'VOID') AND e.posted_at IS NOT NULL
            UNION ALL
            SELECT 'Invoice', i.id, 'COGS ' || COALESCE(i.invoice_number, i.id::text), c.cost, i.status = 'POSTED', l.status, l.erpnext_name, l.last_error
            FROM invoices i
            CROSS JOIN LATERAL (SELECT round(COALESCE(sum(x.quantity * x.cost_price_usd), 0), 4) AS cost FROM invoice_lines x WHERE x.invoice_id = i.id) c
            LEFT JOIN erpnext_sync_log l ON l.local_entity_type = 'Invoice' AND l.local_entity_id = i.id AND l.erpnext_doctype = 'Journal Entry'
            WHERE i.invoice_type IN ('SALE', 'RETURN') AND i.status IN ('POSTED', 'VOID') AND i.posted_at IS NOT NULL AND c.cost > 0
            UNION ALL
            SELECT 'StockAdjustment', a.id, a.adjustment_no, NULL::numeric, a.status = 'POSTED' AND COALESCE(l.status, '') <> 'SKIPPED', l.status, l.erpnext_name, l.last_error
            FROM stock_adjustments a
            LEFT JOIN erpnext_sync_log l ON l.local_entity_type = 'StockAdjustment' AND l.local_entity_id = a.id AND l.erpnext_doctype = 'Journal Entry'
            WHERE a.status = 'POSTED'
            UNION ALL
            SELECT 'LandedCost', v.id, v.voucher_number, v.total_usd, v.status = 'POSTED', l.status, l.erpnext_name, l.last_error
            FROM landed_cost_vouchers v
            LEFT JOIN erpnext_sync_log l ON l.local_entity_type = 'LandedCost' AND l.local_entity_id = v.id AND l.erpnext_doctype = 'Journal Entry'
            WHERE v.status IN ('POSTED', 'VOID') AND v.posted_at IS NOT NULL;
            """),
    ];

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IErpNextClient _erpNext;

    public GetErpNextConsistencyQueryHandler(IDbConnectionFactory connectionFactory, IErpNextClient erpNext)
    {
        _connectionFactory = connectionFactory;
        _erpNext = erpNext;
    }

    public async Task<Result<ErpNextConsistencyDto>> Handle(GetErpNextConsistencyQuery request, CancellationToken cancellationToken)
    {
        if (!_erpNext.IsEnabled)
        {
            return Result<ErpNextConsistencyDto>.Failure(new Error("ErpNext.Disabled", "ERPNext integration is disabled."));
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var result = new List<ErpNextConsistencySectionDto>();
        foreach (var section in Sections)
        {
            var local = (await connection.QueryAsync<LocalRecord>(new CommandDefinition(section.Sql, cancellationToken: cancellationToken))).ToList();
            var remote = await _erpNext.GetDocumentIndexAsync(section.Doctype, cancellationToken);
            result.Add(remote.IsFailure
                ? ConsistencyComparer.Unreachable(section.Key, section.Doctype, local, section.WithTotals, remote.Error.Message)
                : ConsistencyComparer.Compare(section.Key, section.Doctype, local,
                    remote.Value!.Select(r => new RemoteRecord(r.Name, r.Amount, r.IsTransaction ? r.DocStatus == 1 : !r.Disabled, r.DocStatus == 2)).ToList(),
                    section.WithTotals));
        }

        return Result<ErpNextConsistencyDto>.Success(new ErpNextConsistencyDto(DateTimeOffset.UtcNow, result));
    }

    private static string Party(string typeCode, string doctype) => $"""
        SELECT 'Party' AS EntityType, p.id AS Id, COALESCE(NULLIF(p.display_name, ''), p.display_name_ar) AS Ref, NULL::numeric AS Amount,
               (p.is_active AND t.is_active) AS Active, l.status AS SyncStatus, l.erpnext_name AS ErpNextName, l.last_error AS LastError
        FROM parties p
        INNER JOIN party_type_assignments t ON t.party_id = p.id AND t.type_code = '{typeCode}'
        LEFT JOIN erpnext_sync_log l ON l.local_entity_type = 'Party' AND l.local_entity_id = p.id AND l.erpnext_doctype = '{doctype}';
        """;
}

/// <summary>A read-only ERPNext list with accounting meaning; the kinds are fixed (this is not a document browser).</summary>
public sealed record GetErpNextReferenceQuery(string Kind) : IRequest<Result<ErpNextReferenceDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetErpNextReferenceQueryHandler : IRequestHandler<GetErpNextReferenceQuery, Result<ErpNextReferenceDto>>
{
    private readonly IErpNextClient _erpNext;
    private readonly IDbConnectionFactory _connectionFactory;

    public GetErpNextReferenceQueryHandler(IErpNextClient erpNext, IDbConnectionFactory connectionFactory)
    {
        _erpNext = erpNext;
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<ErpNextReferenceDto>> Handle(GetErpNextReferenceQuery request, CancellationToken cancellationToken)
    {
        var list = await _erpNext.GetReferenceListAsync(request.Kind, cancellationToken);
        if (list.IsFailure)
        {
            return Result<ErpNextReferenceDto>.Failure(list.Error);
        }

        if (request.Kind != "exchange-rates")
        {
            return Result<ErpNextReferenceDto>.Success(new ErpNextReferenceDto(request.Kind, list.Value!.Doctype, list.Value.Columns, list.Value.Rows));
        }

        // The matching local record of an ERPNext rate is our saved rate for the same day (the latest one saved that day).
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var local = (await connection.QueryAsync<(DateOnly Day, decimal Mid)>(new CommandDefinition(
            "SELECT DISTINCT ON (rate_date) rate_date AS Day, mid_rate AS Mid FROM fx_rates ORDER BY rate_date, created_at DESC;",
            cancellationToken: cancellationToken))).ToDictionary(x => x.Day.ToString("yyyy-MM-dd"), x => x.Mid);
        var rows = list.Value!.Rows
            .Select(r => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>(r)
            {
                ["local_mid_rate"] = r.TryGetValue("date", out var d) && d is not null && local.TryGetValue(d[..Math.Min(10, d.Length)], out var mid)
                    ? mid.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : null
            })
            .ToList();
        return Result<ErpNextReferenceDto>.Success(new ErpNextReferenceDto(request.Kind, list.Value.Doctype, [.. list.Value.Columns, "local_mid_rate"], rows));
    }
}

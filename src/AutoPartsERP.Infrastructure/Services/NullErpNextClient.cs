namespace AutoPartsERP.Infrastructure.Services;

/// <summary>
/// Default IErpNextClient registration: ERPNext is not deployed yet (pending the VPS upgrade), so
/// every sync call is a safe no-op instead of a network call to a host that doesn't exist. Swap for
/// a real HTTP-based implementation once ErpNext:Enabled is set - no call site changes needed.
/// </summary>
public sealed class NullErpNextClient : IErpNextClient
{
    private readonly ILogger<NullErpNextClient> _logger;

    public NullErpNextClient(ILogger<NullErpNextClient> logger)
    {
        _logger = logger;
    }

    public bool IsEnabled => false;

    public Task<Result<string>> SyncItemAsync(ErpNextItemSync item, CancellationToken cancellationToken = default) =>
        Skip(nameof(SyncItemAsync), item.LocalItemId);

    public Task<Result<string>> SyncSalesPersonAsync(ErpNextSalesPersonSync person, CancellationToken cancellationToken = default) =>
        Skip(nameof(SyncSalesPersonAsync), person.LocalUserId);

    public Task<Result<string>> SyncPartyAsync(ErpNextPartySync party, CancellationToken cancellationToken = default) =>
        Skip(nameof(SyncPartyAsync), party.LocalPartyId);

    public Task<Result<string>> SyncSalesInvoiceAsync(ErpNextSalesInvoiceSync invoice, CancellationToken cancellationToken = default) =>
        Skip(nameof(SyncSalesInvoiceAsync), invoice.LocalInvoiceId);

    public Task<Result<string>> SyncPaymentAsync(ErpNextPaymentSync payment, CancellationToken cancellationToken = default) =>
        Skip(nameof(SyncPaymentAsync), payment.LocalPaymentId);

    public Task<Result<string>> SyncCogsEntryAsync(ErpNextCogsEntrySync entry, CancellationToken cancellationToken = default) =>
        Skip(nameof(SyncCogsEntryAsync), entry.LocalInvoiceId);

    public Task<Result<string>> SyncPurchaseInvoiceAsync(ErpNextPurchaseInvoiceSync bill, CancellationToken cancellationToken = default) =>
        Skip(nameof(SyncPurchaseInvoiceAsync), bill.LocalId);

    public Task<Result<string>> SyncLandedCostEntryAsync(ErpNextLandedCostSync entry, CancellationToken cancellationToken = default) =>
        Skip(nameof(SyncLandedCostEntryAsync), entry.LocalId);

    public Task<Result<string>> SyncSupplierPaymentAsync(ErpNextSupplierPaymentSync payment, CancellationToken cancellationToken = default) =>
        Skip(nameof(SyncSupplierPaymentAsync), payment.LocalPaymentId);

    public Task<Result<string>> CancelDocumentAsync(string doctype, string name, CancellationToken cancellationToken = default) =>
        Skip(nameof(CancelDocumentAsync), Guid.Empty);

    public Task<Result<IReadOnlyList<ErpNextAccount>>> GetChartOfAccountsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<ErpNextAccount>>.Failure(new Error("ErpNext.Disabled", "ERPNext integration is disabled.")));

    public Task<Result<IReadOnlyList<ErpNextAccountMapping>>> GetAccountMappingAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<ErpNextAccountMapping>>.Failure(new Error("ErpNext.Disabled", "ERPNext integration is disabled.")));

    public Task<Result<IReadOnlyList<ErpNextIndexRow>>> GetDocumentIndexAsync(string doctype, CancellationToken cancellationToken = default) =>
        Disabled<IReadOnlyList<ErpNextIndexRow>>();

    public Task<Result<ErpNextReferenceList>> GetReferenceListAsync(string kind, CancellationToken cancellationToken = default) =>
        Disabled<ErpNextReferenceList>();

    public Task<Result<string>> CreateAccountAsync(ErpNextAccountCreate account, CancellationToken cancellationToken = default) => Disabled<string>();

    public Task<Result<string>> UpdateAccountAsync(string name, ErpNextAccountUpdate update, CancellationToken cancellationToken = default) => Disabled<string>();

    public Task<Result<string>> SyncJournalEntryAsync(ErpNextJournalEntrySync entry, CancellationToken cancellationToken = default) =>
        Skip(nameof(SyncJournalEntryAsync), entry.LocalId);

    public Task<Result<IReadOnlyList<ErpNextGlBalance>>> GetGlBalancesAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default) =>
        Disabled<IReadOnlyList<ErpNextGlBalance>>();

    public Task<Result<IReadOnlyList<ErpNextGlEntry>>> GetGlEntriesAsync(ErpNextGlFilter filter, CancellationToken cancellationToken = default) =>
        Disabled<IReadOnlyList<ErpNextGlEntry>>();

    public Task<Result<ErpNextGlSummary>> GetGlSummaryAsync(ErpNextGlFilter filter, CancellationToken cancellationToken = default) => Disabled<ErpNextGlSummary>();

    public Task<Result<ErpNextGlSummary>> GetGlOffsetSummaryAsync(ErpNextGlFilter filter, int skip, CancellationToken cancellationToken = default) => Disabled<ErpNextGlSummary>();

    public Task<Result<IReadOnlyList<ErpNextPartyBalance>>> GetPartyBalancesAsync(string partyType, DateOnly asOf, CancellationToken cancellationToken = default) =>
        Disabled<IReadOnlyList<ErpNextPartyBalance>>();

    public Task<Result<IReadOnlyList<ErpNextOpenInvoice>>> GetOpenInvoicesAsync(string doctype, DateOnly asOf, CancellationToken cancellationToken = default) =>
        Disabled<IReadOnlyList<ErpNextOpenInvoice>>();

    private static Task<Result<T>> Disabled<T>() => Task.FromResult(Result<T>.Failure(new Error("ErpNext.Disabled", "ERPNext integration is disabled.")));

    private Task<Result<string>> Skip(string operation, Guid localId)
    {
        _logger.LogDebug("ERPNext sync skipped (not configured): {Operation} for {LocalId}", operation, localId);
        return Task.FromResult(Result<string>.Success(string.Empty));
    }
}

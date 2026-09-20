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

    public Task<Result<string>> SyncSupplierPaymentAsync(ErpNextSupplierPaymentSync payment, CancellationToken cancellationToken = default) =>
        Skip(nameof(SyncSupplierPaymentAsync), payment.LocalPaymentId);

    public Task<Result<string>> RenameDocumentAsync(string doctype, string oldName, string newName, CancellationToken cancellationToken = default) =>
        Skip(nameof(RenameDocumentAsync), Guid.Empty);

    public Task<Result<string>> CancelDocumentAsync(string doctype, string name, CancellationToken cancellationToken = default) =>
        Skip(nameof(CancelDocumentAsync), Guid.Empty);

    public Task<Result<IReadOnlyList<ErpNextAccount>>> GetChartOfAccountsAsync(bool includeBalances, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<ErpNextAccount>>.Failure(new Error("ErpNext.Disabled", "ERPNext integration is disabled.")));

    public Task<Result<IReadOnlyList<ErpNextAccountMapping>>> GetAccountMappingAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<ErpNextAccountMapping>>.Failure(new Error("ErpNext.Disabled", "ERPNext integration is disabled.")));

    public Task<Result<ErpNextDocumentPage>> ListDocumentsAsync(string doctype, int page, int pageSize, string? search, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<ErpNextDocumentPage>.Failure(new Error("ErpNext.Disabled", "ERPNext integration is disabled.")));

    private Task<Result<string>> Skip(string operation, Guid localId)
    {
        _logger.LogDebug("ERPNext sync skipped (not configured): {Operation} for {LocalId}", operation, localId);
        return Task.FromResult(Result<string>.Success(string.Empty));
    }
}

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

    public Task<Result<string>> CancelDocumentAsync(string doctype, string name, CancellationToken cancellationToken = default) =>
        Skip(nameof(CancelDocumentAsync), Guid.Empty);

    private Task<Result<string>> Skip(string operation, Guid localId)
    {
        _logger.LogDebug("ERPNext sync skipped (not configured): {Operation} for {LocalId}", operation, localId);
        return Task.FromResult(Result<string>.Success(string.Empty));
    }
}

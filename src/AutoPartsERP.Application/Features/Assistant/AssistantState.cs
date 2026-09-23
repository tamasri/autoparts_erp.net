namespace AutoPartsERP.Application.Features.Assistant;

public sealed record PendingOption(Guid Id, string Label);

/// <summary>A question waiting for the user to pick which record they meant ("1", "2", …); kept for a few minutes.</summary>
public sealed record PendingChoice(string Action, Dictionary<string, string> Args, IReadOnlyList<PendingOption> Options);

/// <summary>What the WhatsApp gateway last reported: connected, waiting for a QR scan (with the QR text), or disconnected.</summary>
public sealed record GatewayStatus(string State, string? Qr, string? Account, DateTimeOffset ReportedAt);

/// <summary>Short-lived assistant state, kept in Redis (shared by all API instances, gone after its time-to-live).</summary>
public interface IAssistantState
{
    /// <summary>True the first time a WhatsApp message id is seen (the gateway may deliver a message twice).</summary>
    Task<bool> FirstSeenAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>Counts one hit for <paramref name="key"/>; false once more than <paramref name="limit"/> in <paramref name="window"/>.</summary>
    Task<bool> AllowAsync(string key, int limit, TimeSpan window, CancellationToken cancellationToken = default);

    Task<PendingChoice?> GetPendingAsync(Guid linkId, CancellationToken cancellationToken = default);

    Task SetPendingAsync(Guid linkId, PendingChoice choice, CancellationToken cancellationToken = default);

    Task ClearPendingAsync(Guid linkId, CancellationToken cancellationToken = default);

    Task<GatewayStatus?> GetGatewayStatusAsync(CancellationToken cancellationToken = default);

    Task SetGatewayStatusAsync(GatewayStatus status, CancellationToken cancellationToken = default);
}

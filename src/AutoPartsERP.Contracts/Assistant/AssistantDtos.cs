namespace AutoPartsERP.Contracts.Assistant;

public sealed record AssistantLinkDto(
    Guid Id,
    Guid UserId,
    string UserName,
    string FullName,
    string Phone,
    string Status,
    bool UserHasAccess,
    DateTimeOffset CreatedAt,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? CodeExpiresAt);

/// <summary>Shown once to the administrator: the phone must send this code to the assistant's WhatsApp number to finish linking.</summary>
public sealed record AssistantLinkCodeDto(Guid LinkId, string Phone, string Code, DateTimeOffset ExpiresAt);

public sealed record AssistantStatusDto(
    bool Enabled,
    bool ModelConfigured,
    string ModelName,
    string GatewayState,
    string? GatewayQr,
    string? GatewayAccount,
    DateTimeOffset? GatewayReportedAt);

public sealed record CreateAssistantLinkRequest(Guid UserId, string Phone);

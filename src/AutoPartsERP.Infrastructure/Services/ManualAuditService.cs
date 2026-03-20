namespace AutoPartsERP.Infrastructure.Services;

public sealed class ManualAuditService : IManualAuditService
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public ManualAuditService(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task LogAsync(ManualAuditEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dbConnectionFactory.CreateAsync(cancellationToken);
        await connection.ExecuteAsync(
            "INSERT INTO audit_logs (id, correlation_id, event_type, module, entity_type, entity_id, actor_id, actor_username, action, reason_code, reason_notes, ip_address, user_agent, status, rejection_reason, created_at) VALUES (@Id, @CorrelationId, @EventType, @Module, @EntityType, @EntityId, @ActorId, @ActorUsername, @Action, @ReasonCode, @ReasonNotes, @IpAddress, @UserAgent, @Status, @RejectionReason, @CreatedAt);",
            new
            {
                Id = Guid.NewGuid(),
                entry.CorrelationId,
                entry.EventType,
                entry.Module,
                entry.EntityType,
                EntityId = entry.EntityId?.ToString(),
                entry.ActorId,
                entry.ActorUsername,
                entry.Action,
                entry.ReasonCode,
                entry.ReasonNotes,
                entry.IpAddress,
                entry.UserAgent,
                entry.Status,
                entry.RejectionReason,
                CreatedAt = DateTimeOffset.UtcNow
            });
    }

    public async Task LogRejectionAsync(RejectionEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dbConnectionFactory.CreateAsync(cancellationToken);
        await connection.ExecuteAsync(
            "INSERT INTO rejection_attempts (id, correlation_id, user_id, username, endpoint, permission_required, reason, ip_address, created_at) VALUES (@Id, @CorrelationId, @UserId, @Username, @Endpoint, @PermissionRequired, @Reason, @IpAddress, @CreatedAt);",
            new
            {
                Id = Guid.NewGuid(),
                entry.CorrelationId,
                entry.UserId,
                entry.Username,
                entry.Endpoint,
                entry.PermissionRequired,
                entry.Reason,
                entry.IpAddress,
                CreatedAt = DateTimeOffset.UtcNow
            });
    }
}
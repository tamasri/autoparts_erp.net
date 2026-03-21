namespace AutoPartsERP.Application.Features.InventoryAlerts;

public sealed record GetInventoryAlertsQuery()
    : IRequest<Result<IReadOnlyCollection<InventoryAlertDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.InventoryAlerts.Read;
}

public sealed class GetInventoryAlertsQueryHandler : IRequestHandler<GetInventoryAlertsQuery, Result<IReadOnlyCollection<InventoryAlertDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetInventoryAlertsQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<IReadOnlyCollection<InventoryAlertDto>>> Handle(GetInventoryAlertsQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<InventoryAlertDto>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    item_id AS ItemId,
                    alert_type AS AlertType,
                    severity AS Severity,
                    message AS Message,
                    threshold_value AS ThresholdValue,
                    current_value AS CurrentValue,
                    status AS Status,
                    acknowledged_by AS AcknowledgedBy,
                    acknowledged_at AS AcknowledgedAt,
                    resolved_by AS ResolvedBy,
                    resolved_at AS ResolvedAt,
                    resolution_note AS ResolutionNote,
                    created_at AS CreatedAt
                FROM inventory_alerts
                ORDER BY created_at DESC;
                """,
                cancellationToken: cancellationToken));

        return Result<IReadOnlyCollection<InventoryAlertDto>>.Success(rows.ToArray());
    }
}

public sealed record AcknowledgeAlertCommand(Guid AlertId)
    : IRequest<Result>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.InventoryAlerts.Acknowledge;
    public string AuditModule => "INVENTORY";
}

public sealed class AcknowledgeAlertCommandHandler : IRequestHandler<AcknowledgeAlertCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public AcknowledgeAlertCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(AcknowledgeAlertCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE inventory_alerts
                SET status = 'ACKNOWLEDGED',
                    acknowledged_by = @UserId,
                    acknowledged_at = now()
                WHERE id = @Id
                  AND status = 'OPEN';
                """,
                new
                {
                    Id = request.AlertId,
                    UserId = _currentUser.UserId
                },
                cancellationToken: cancellationToken));

        return affected == 0
            ? Result.Failure(new Error("InventoryAlert.NotFound", "Alert not found or already acknowledged."))
            : Result.Success();
    }
}

public sealed record ResolveAlertCommand(Guid AlertId, ResolveInventoryAlertRequest Request)
    : IRequest<Result>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.InventoryAlerts.Resolve;
    public string AuditModule => "INVENTORY";
}

public sealed class ResolveAlertCommandHandler : IRequestHandler<ResolveAlertCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public ResolveAlertCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(ResolveAlertCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE inventory_alerts
                SET status = 'RESOLVED',
                    resolved_by = @UserId,
                    resolved_at = now(),
                    resolution_note = @ResolutionNote
                WHERE id = @Id
                  AND status <> 'RESOLVED';
                """,
                new
                {
                    Id = request.AlertId,
                    UserId = _currentUser.UserId,
                    request.Request.ResolutionNote
                },
                cancellationToken: cancellationToken));

        return affected == 0
            ? Result.Failure(new Error("InventoryAlert.NotFound", "Alert not found or already resolved."))
            : Result.Success();
    }
}


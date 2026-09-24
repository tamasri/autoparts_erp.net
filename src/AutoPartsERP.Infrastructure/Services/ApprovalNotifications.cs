namespace AutoPartsERP.Infrastructure.Services;

/// <summary>
/// Real-time notice of approval activity. A new request goes to everyone who reviews requests (<c>approvals.review</c>) and, for a
/// transfer, to the managers of the warehouses it touches (they may approve it without that permission). A decision goes back to
/// the requester. Payloads carry codes and ids only; the browser shows its own labels and opens the approvals screen for details.
/// </summary>
public sealed class ApprovalNotifications
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IRealtimeNotifier _notifier;
    private readonly ILogger<ApprovalNotifications> _logger;

    public ApprovalNotifications(IDbConnectionFactory connectionFactory, IRealtimeNotifier notifier, ILogger<ApprovalNotifications> logger)
    {
        _connectionFactory = connectionFactory;
        _notifier = notifier;
        _logger = logger;
    }

    private sealed record RequestRow(string ActionCode, Guid RequesterId, string RequesterName, int PendingCount);

    public async Task RequestedAsync(Guid approvalId, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            var row = await connection.QuerySingleOrDefaultAsync<RequestRow>(new CommandDefinition(
                """
                SELECT r.action_code AS ActionCode, r.requested_by_user_id AS RequesterId,
                       COALESCE(NULLIF(u.full_name, ''), u.user_name, '') AS RequesterName,
                       (SELECT count(*)::int FROM approval_requests p WHERE p.status IN ('PENDING', 'IN_REVIEW')) AS PendingCount
                FROM approval_requests r LEFT JOIN asp_net_users u ON u.id = r.requested_by_user_id
                WHERE r.id = @approvalId;
                """,
                new { approvalId }, cancellationToken: cancellationToken));
            if (row is null)
            {
                return;
            }

            var payload = new { approvalId, row.ActionCode, row.RequesterId, row.RequesterName, row.PendingCount };
            await _notifier.ToPermissionAsync(PermissionCodes.ApprovalsReview, RealtimeEvents.NewApprovalRequest, payload, cancellationToken);

            // uuid[] is read on its own: Dapper cannot hand an array column to a positional record's constructor.
            var scope = await connection.ExecuteScalarAsync<Guid[]?>(new CommandDefinition(
                "SELECT scope_warehouse_ids FROM approval_requests WHERE id = @approvalId;", new { approvalId }, cancellationToken: cancellationToken));
            if (scope is { Length: > 0 })
            {
                var managers = await connection.QueryAsync<Guid>(new CommandDefinition(
                    "SELECT DISTINCT user_id FROM user_warehouses WHERE is_manager AND warehouse_id = ANY(@scope) AND user_id <> @RequesterId;",
                    new { scope, row.RequesterId }, cancellationToken: cancellationToken));
                await _notifier.ToUsersAsync(managers, RealtimeEvents.NewApprovalRequest, payload, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Approval {ApprovalId}: new-request notice not sent.", approvalId);
        }
    }

    public async Task DecidedAsync(Guid approvalId, string actionCode, Guid requesterId, string status, Guid reviewerId, string? comment, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            var reviewerName = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT COALESCE(NULLIF(full_name, ''), user_name) FROM asp_net_users WHERE id = @reviewerId;", new { reviewerId }, cancellationToken: cancellationToken));
            await _notifier.ToUsersAsync([requesterId], RealtimeEvents.ApprovalDecided,
                new { approvalId, actionCode, status, reviewerName, comment }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Approval {ApprovalId}: decision notice not sent.", approvalId);
        }
    }
}

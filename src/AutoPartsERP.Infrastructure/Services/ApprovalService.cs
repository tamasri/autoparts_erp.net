using AutoPartsERP.Infrastructure.Persistence;

namespace AutoPartsERP.Infrastructure.Services;

public sealed class ApprovalService : IApprovalService
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly AppDbContext _dbContext;

    public ApprovalService(IDbConnectionFactory dbConnectionFactory, AppDbContext dbContext)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _dbContext = dbContext;
    }

    public async Task<Result<Guid>> CreatePendingApprovalAsync(PendingApprovalSubmission submission, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        await using var connection = await _dbConnectionFactory.CreateAsync(cancellationToken);
        await connection.ExecuteAsync(
            // Populate BOTH column generations: the original hand-written ones (requester_id, created_at, ...) and the
            // ones FixApprovalGovernanceSchema made NOT NULL for the EF ApprovalRequest entity. Omitting the latter made
            // every maker-checker submission fail with a not-null violation.
            """
            INSERT INTO approval_requests (
                id, correlation_id, request_type, entity_type, entity_id, payload_json,
                requester_id, requester_notes, reason_code, status, expires_at, created_at,
                action_code, requested_by_user_id, reason, required_approvals, requested_at_utc, created_at_utc)
            VALUES (
                @Id, @CorrelationId, @RequestType, @EntityType, COALESCE(@EntityId, ''), CAST(@PayloadJson AS jsonb),
                @RequesterId, @RequesterNotes, @ReasonCode, 'PENDING', @ExpiresAt, @CreatedAt,
                @RequestType, @RequesterId, COALESCE(@RequesterNotes, ''), 1, @CreatedAt, @CreatedAt);
            """,
            new
            {
                Id = id,
                submission.CorrelationId,
                submission.RequestType,
                submission.EntityType,
                submission.EntityId,
                submission.PayloadJson,
                submission.RequesterId,
                submission.RequesterNotes,
                submission.ReasonCode,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(48),
                CreatedAt = DateTimeOffset.UtcNow
            });

        return Result<Guid>.Success(id);
    }

    public async Task SaveAsync(ApprovalRequest approvalRequest, CancellationToken cancellationToken = default)
    {
        if (_dbContext.Entry(approvalRequest).State == EntityState.Detached)
        {
            _dbContext.ApprovalRequests.Add(approvalRequest);
        }
        else
        {
            _dbContext.ApprovalRequests.Update(approvalRequest);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
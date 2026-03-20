using AutoPartsERP.Infrastructure.Persistence;

namespace AutoPartsERP.Infrastructure.Services;

public sealed class GovernanceService : IGovernanceService
{
    private readonly AppDbContext _dbContext;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public GovernanceService(AppDbContext dbContext, IDbConnectionFactory dbConnectionFactory)
    {
        _dbContext = dbContext;
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<Result<PagedResponse<ApprovalRequestDto>>> GetApprovalsAsync(ApprovalListFilter filter, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ApprovalRequests.AsNoTracking();
        if (filter.ExcludeCurrentUserRequests && filter.CurrentUserId.HasValue)
        {
            query = query.Where(x => x.RequestedByUserId != filter.CurrentUserId.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.RequestedAtUtc)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return Result<PagedResponse<ApprovalRequestDto>>.Success(new PagedResponse<ApprovalRequestDto>(items.Select(ToDto).ToArray(), filter.PageNumber, filter.PageSize, total));
    }

    public async Task<Result<ApprovalRequestDto>> GetApprovalByIdAsync(Guid approvalId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.ApprovalRequests.FirstOrDefaultAsync(x => x.Id == approvalId, cancellationToken);
        if (entity is null)
        {
            return Result<ApprovalRequestDto>.Failure(new Error("Approvals.NotFound", "Approval request was not found."));
        }

        return Result<ApprovalRequestDto>.Success(ToDto(entity));
    }

    public async Task<Result<ApprovalRequestDto>> CreateApprovalAsync(CreateApprovalRequest request, Guid requesterUserId, CancellationToken cancellationToken = default)
    {
        var entity = new ApprovalRequest(Guid.NewGuid(), request.EntityType, request.EntityId, request.ActionCode, requesterUserId, request.Reason, request.RequiredApprovals);
        _dbContext.ApprovalRequests.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<ApprovalRequestDto>.Success(ToDto(entity));
    }

    public async Task<Result<ApprovalRequestDto>> ApproveApprovalAsync(Guid approvalId, string? comment, Guid reviewerUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.ApprovalRequests.FirstOrDefaultAsync(x => x.Id == approvalId, cancellationToken);
        if (entity is null)
        {
            return Result<ApprovalRequestDto>.Failure(new Error("Approvals.NotFound", "Approval request was not found."));
        }

        var result = entity.Approve(reviewerUserId, comment);
        if (result.IsFailure)
        {
            return Result<ApprovalRequestDto>.Failure(result.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<ApprovalRequestDto>.Success(ToDto(entity));
    }

    public async Task<Result<ApprovalRequestDto>> RejectApprovalAsync(Guid approvalId, string comment, Guid reviewerUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.ApprovalRequests.FirstOrDefaultAsync(x => x.Id == approvalId, cancellationToken);
        if (entity is null)
        {
            return Result<ApprovalRequestDto>.Failure(new Error("Approvals.NotFound", "Approval request was not found."));
        }

        var result = entity.Reject(reviewerUserId, comment);
        if (result.IsFailure)
        {
            return Result<ApprovalRequestDto>.Failure(result.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<ApprovalRequestDto>.Success(ToDto(entity));
    }

    public async Task<Result<ApprovalRequestDto>> CancelApprovalAsync(Guid approvalId, string reason, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.ApprovalRequests.FirstOrDefaultAsync(x => x.Id == approvalId, cancellationToken);
        if (entity is null)
        {
            return Result<ApprovalRequestDto>.Failure(new Error("Approvals.NotFound", "Approval request was not found."));
        }

        var result = entity.Cancel(reason);
        if (result.IsFailure)
        {
            return Result<ApprovalRequestDto>.Failure(result.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<ApprovalRequestDto>.Success(ToDto(entity));
    }

    public async Task<Result<IReadOnlyCollection<PeriodLockDto>>> GetPeriodLocksAsync(PeriodLockFilter filter, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.PeriodLocks.AsNoTracking().AsQueryable();
        if (filter.Year.HasValue && filter.Month.HasValue)
        {
            var period = $"{filter.Year.Value:D4}-{filter.Month.Value:D2}";
            query = query.Where(x => x.PeriodKey == period);
        }

        if (!string.IsNullOrWhiteSpace(filter.ModuleCode))
        {
            query = query.Where(x => x.ModuleCode == filter.ModuleCode);
        }

        var items = await query.OrderByDescending(x => x.LockedAtUtc).ToListAsync(cancellationToken);
        return Result<IReadOnlyCollection<PeriodLockDto>>.Success(items.Select(ToDto).ToArray());
    }

    public async Task<Result<PeriodLockDto>> LockPeriodAsync(LockPeriodRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = new PeriodLock(Guid.NewGuid(), request.PeriodKey, request.ModuleCode, actorUserId, request.Reason);
        _dbContext.PeriodLocks.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<PeriodLockDto>.Success(ToDto(entity));
    }

    public async Task<Result<PeriodLockDto>> UnlockPeriodAsync(Guid periodLockId, UnlockPeriodRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.PeriodLocks.FirstOrDefaultAsync(x => x.Id == periodLockId, cancellationToken);
        if (entity is null)
        {
            return Result<PeriodLockDto>.Failure(new Error("Periods.NotFound", "Period lock was not found."));
        }

        entity.Unlock(actorUserId, request.Reason);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<PeriodLockDto>.Success(ToDto(entity));
    }

    public async Task<Result<IReadOnlyCollection<ReasonCodeDto>>> GetReasonCodesAsync(ReasonCodeFilter filter, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ReasonCodes.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            query = query.Where(x => x.Category == filter.Category);
        }

        if (filter.IsActive.HasValue)
        {
            query = query.Where(x => x.IsActive == filter.IsActive.Value);
        }

        var items = await query.OrderBy(x => x.Code).ToListAsync(cancellationToken);
        return Result<IReadOnlyCollection<ReasonCodeDto>>.Success(items.Select(ToDto).ToArray());
    }

    public async Task<Result<ReasonCodeDto>> GetReasonCodeByIdAsync(Guid reasonCodeId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.ReasonCodes.FirstOrDefaultAsync(x => x.Id == reasonCodeId, cancellationToken);
        if (entity is null)
        {
            return Result<ReasonCodeDto>.Failure(new Error("ReasonCodes.NotFound", "Reason code was not found."));
        }

        return Result<ReasonCodeDto>.Success(ToDto(entity));
    }

    public async Task<Result<ReasonCodeDto>> CreateReasonCodeAsync(CreateReasonCodeRequest request, CancellationToken cancellationToken = default)
    {
        var entity = new ReasonCode(Guid.NewGuid(), request.Category, request.Code, request.Description, request.RequiresComment, request.AppliesTo);
        _dbContext.ReasonCodes.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<ReasonCodeDto>.Success(ToDto(entity));
    }

    public async Task<Result<ReasonCodeDto>> UpdateReasonCodeAsync(Guid reasonCodeId, UpdateReasonCodeRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.ReasonCodes.FirstOrDefaultAsync(x => x.Id == reasonCodeId, cancellationToken);
        if (entity is null)
        {
            return Result<ReasonCodeDto>.Failure(new Error("ReasonCodes.NotFound", "Reason code was not found."));
        }

        entity.Update(request.Category, request.Description, request.RequiresComment, request.AppliesTo);
        if (request.IsActive)
        {
            entity.Activate();
        }
        else
        {
            entity.Deactivate();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<ReasonCodeDto>.Success(ToDto(entity));
    }

    public async Task<Result<PagedResponse<AuditEntryDto>>> SearchAuditAsync(AuditSearchRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dbConnectionFactory.CreateAsync(cancellationToken);

        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(request.Action)) { conditions.Add("action = @Action"); parameters.Add("Action", request.Action); }
        if (!string.IsNullOrWhiteSpace(request.EntityType)) { conditions.Add("entity_type = @EntityType"); parameters.Add("EntityType", request.EntityType); }
        if (!string.IsNullOrWhiteSpace(request.EntityId)) { conditions.Add("entity_id = @EntityId"); parameters.Add("EntityId", request.EntityId); }
        if (request.ActorUserId.HasValue) { conditions.Add("actor_id = @ActorId"); parameters.Add("ActorId", request.ActorUserId.Value); }
        if (request.FromUtc.HasValue) { conditions.Add("created_at >= @FromUtc"); parameters.Add("FromUtc", request.FromUtc.Value); }
        if (request.ToUtc.HasValue) { conditions.Add("created_at <= @ToUtc"); parameters.Add("ToUtc", request.ToUtc.Value); }

        var whereClause = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
        var offset = (request.PageNumber - 1) * request.PageSize;
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", request.PageSize);

        var rows = (await connection.QueryAsync<AuditEntryDto>(
            $"SELECT id, action, entity_type AS EntityType, entity_id AS EntityId, actor_id AS ActorUserId, actor_username AS ActorName, correlation_id::text AS CorrelationId, ip_address AS IpAddress, reason_notes AS Details, created_at AS OccurredAtUtc FROM audit_logs {whereClause} ORDER BY created_at DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;",
            parameters)).ToArray();

        var total = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM audit_logs {whereClause};", parameters);

        return Result<PagedResponse<AuditEntryDto>>.Success(new PagedResponse<AuditEntryDto>(rows, request.PageNumber, request.PageSize, total));
    }

    public async Task<Result<AuditEntryDto>> GetAuditEntryByIdAsync(Guid auditEntryId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dbConnectionFactory.CreateAsync(cancellationToken);
        var item = await connection.QueryFirstOrDefaultAsync<AuditEntryDto>(
            "SELECT id, action, entity_type AS EntityType, entity_id AS EntityId, actor_id AS ActorUserId, actor_username AS ActorName, correlation_id::text AS CorrelationId, ip_address AS IpAddress, reason_notes AS Details, created_at AS OccurredAtUtc FROM audit_logs WHERE id = @Id;",
            new { Id = auditEntryId });

        return item is null
            ? Result<AuditEntryDto>.Failure(new Error("Audit.NotFound", "Audit log entry was not found."))
            : Result<AuditEntryDto>.Success(item);
    }

    public async Task<Result<IReadOnlyCollection<AuditEntryDto>>> GetEntityAuditTrailAsync(string entityType, string entityId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dbConnectionFactory.CreateAsync(cancellationToken);
        var rows = (await connection.QueryAsync<AuditEntryDto>(
            "SELECT id, action, entity_type AS EntityType, entity_id AS EntityId, actor_id AS ActorUserId, actor_username AS ActorName, correlation_id::text AS CorrelationId, ip_address AS IpAddress, reason_notes AS Details, created_at AS OccurredAtUtc FROM audit_logs WHERE entity_type = @EntityType AND entity_id = @EntityId ORDER BY created_at DESC;",
            new { EntityType = entityType, EntityId = entityId })).ToArray();

        return Result<IReadOnlyCollection<AuditEntryDto>>.Success(rows);
    }

    private static ApprovalRequestDto ToDto(ApprovalRequest approval)
    {
        return new ApprovalRequestDto(
            approval.Id,
            approval.EntityType,
            approval.EntityId,
            approval.ActionCode,
            approval.Reason,
            approval.Status,
            approval.RequestedByUserId,
            approval.RequiredApprovals,
            approval.CurrentApprovals,
            approval.RequestedAtUtc,
            approval.CompletedAtUtc,
            approval.Decisions.Select(x => new ApprovalDecisionDto(x.Id, x.ReviewerUserId, x.Status, x.Comment, x.ReviewedAtUtc)).ToArray());
    }

    private static PeriodLockDto ToDto(PeriodLock periodLock)
    {
        return new PeriodLockDto(
            periodLock.Id,
            periodLock.PeriodKey,
            periodLock.ModuleCode,
            periodLock.Reason,
            periodLock.IsLocked,
            periodLock.LockedByUserId,
            periodLock.LockedAtUtc,
            periodLock.UnlockedByUserId,
            periodLock.UnlockedAtUtc);
    }

    private static ReasonCodeDto ToDto(ReasonCode reasonCode)
    {
        return new ReasonCodeDto(
            reasonCode.Id,
            reasonCode.Category,
            reasonCode.Code,
            reasonCode.Description,
            reasonCode.RequiresComment,
            reasonCode.AppliesTo,
            reasonCode.IsActive,
            reasonCode.CreatedAtUtc,
            reasonCode.UpdatedAtUtc);
    }
}
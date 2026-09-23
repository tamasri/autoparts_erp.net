using MediatR;
using AutoPartsERP.Application.Common.Governance;
using AutoPartsERP.Infrastructure.Persistence;

namespace AutoPartsERP.Infrastructure.Services;

public sealed class GovernanceService : IGovernanceService
{
    private readonly AppDbContext _dbContext;
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly IMediator _mediator;
    private readonly IApprovalReplayContext _replayContext;
    private readonly ILogger<GovernanceService> _logger;
    private readonly ICurrentUser _currentUser;
    private readonly IWarehouseAccess _warehouses;
    private readonly IManualAuditService _audit;
    private readonly bool _allowSelfApproval;

    public GovernanceService(
        AppDbContext dbContext,
        IDbConnectionFactory dbConnectionFactory,
        IMediator mediator,
        IApprovalReplayContext replayContext,
        ILogger<GovernanceService> logger,
        ICurrentUser currentUser,
        IWarehouseAccess warehouses,
        IManualAuditService audit,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _dbContext = dbContext;
        _dbConnectionFactory = dbConnectionFactory;
        _mediator = mediator;
        _replayContext = replayContext;
        _logger = logger;
        _currentUser = currentUser;
        _warehouses = warehouses;
        _audit = audit;

        // Maker-checker means a second person approves. Single-operator installs can opt out explicitly with
        // Governance:AllowSelfApproval=true (env Governance__AllowSelfApproval); the default is the safe one.
        _allowSelfApproval = configuration.GetValue<bool>("Governance:AllowSelfApproval");
    }

    public async Task<Result<PagedResponse<ApprovalRequestDto>>> GetApprovalsAsync(ApprovalListFilter filter, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ApprovalRequests.AsNoTracking();
        if (!_currentUser.HasPermission(PermissionCodes.ApprovalsRead))
        {
            var managed = await _warehouses.ManagedWarehousesAsync(_currentUser.UserId, cancellationToken);
            if (managed.Count == 0)
            {
                return Result<PagedResponse<ApprovalRequestDto>>.Failure(new Error("Authorization.Forbidden", $"Permission '{PermissionCodes.ApprovalsRead}' is required."));
            }

            var visible = await ApprovalsTouchingAsync(managed.ToArray(), cancellationToken);
            query = query.Where(x => visible.Contains(x.Id));
        }

        if (filter.ExcludeCurrentUserRequests && !_allowSelfApproval && filter.CurrentUserId.HasValue)
        {
            query = query.Where(x => x.RequestedByUserId != filter.CurrentUserId.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.RequestedAtUtc)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var names = await WarehouseNamesAsync(items.Select(x => x.Id).ToArray(), cancellationToken);
        return Result<PagedResponse<ApprovalRequestDto>>.Success(new PagedResponse<ApprovalRequestDto>(
            items.Select(x => ToDto(x, names.GetValueOrDefault(x.Id))).ToArray(), filter.PageNumber, filter.PageSize, total));
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

        if (!_allowSelfApproval && entity.RequestedByUserId == reviewerUserId)
        {
            return Result<ApprovalRequestDto>.Failure(new Error("approval.self-approval-forbidden", "You cannot approve a request you submitted; another approver must review it."));
        }

        var review = await AuthorizeReviewAsync(entity, approving: true, cancellationToken);
        if (review.IsFailure)
        {
            return Result<ApprovalRequestDto>.Failure(review.Error);
        }

        var result = entity.Approve(reviewerUserId, comment, review.Value);
        if (result.IsFailure)
        {
            return Result<ApprovalRequestDto>.Failure(result.Error);
        }

        // The approval only counts if the approved command actually succeeds. Run it BEFORE persisting the decision;
        // on failure discard the in-memory decision so the request stays pending and the approver sees the real error
        // (previously the command result was ignored, so a failed post/void was recorded as an approved success).
        if (string.Equals(entity.Status, ApprovalStatuses.Approved, StringComparison.OrdinalIgnoreCase))
        {
            var replay = await ReplayApprovedRequestAsync(approvalId, cancellationToken);
            if (replay.IsFailure)
            {
                _dbContext.ChangeTracker.Clear();
                _logger.LogError("Approval {ApprovalId}: approved command failed: {Code} {Message}", approvalId, replay.Error.Code, replay.Error.Message);
                return Result<ApprovalRequestDto>.Failure(new Error("approval.execution-failed", $"{replay.Error.Code}: {replay.Error.Message}"));
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<ApprovalRequestDto>.Success(ToDto(entity));
    }

    /// <summary>
    /// Re-dispatches the original MediatR command that <c>MakerCheckerBehavior</c> deferred when it
    /// created this approval request, so approving it actually performs the mutation instead of only
    /// flipping <see cref="ApprovalRequest.Status"/>. Read via Dapper (not the EF <see cref="ApprovalRequest"/>
    /// entity) because <c>request_type</c>/<c>payload_json</c> are written by <c>ApprovalService.CreatePendingApprovalAsync</c>'s
    /// raw SQL insert and are not part of the EF entity model.
    /// </summary>
    private async Task<Result> ReplayApprovedRequestAsync(Guid approvalId, CancellationToken cancellationToken)
    {
        await using var connection = await _dbConnectionFactory.CreateAsync(cancellationToken);
        var payload = await connection.QuerySingleOrDefaultAsync<(string RequestType, string PayloadJson)>(
            new CommandDefinition(
                "SELECT request_type AS RequestType, payload_json::text AS PayloadJson FROM approval_requests WHERE id = @Id;",
                new { Id = approvalId },
                cancellationToken: cancellationToken));

        if (payload.RequestType is null)
        {
            _logger.LogWarning("Approval {ApprovalId} approved but no payload_json/request_type row was found to replay.", approvalId);
            return Result.Failure(new Error("approval.payload-missing", "The approved request has no stored payload to execute."));
        }

        var requestType = ApprovalRequestTypeResolver.Resolve(payload.RequestType);
        if (requestType is null)
        {
            _logger.LogError(
                "Approval {ApprovalId} approved but request type '{RequestType}' could not be resolved to a CLR type; the original command was NOT re-executed.",
                approvalId, payload.RequestType);
            return Result.Failure(new Error("approval.type-unresolved", $"Request type '{payload.RequestType}' could not be resolved."));
        }

        var deserialized = JsonSerializer.Deserialize(payload.PayloadJson, requestType);
        if (deserialized is null)
        {
            _logger.LogError(
                "Approval {ApprovalId} approved but payload_json could not be deserialized into {RequestType}; the original command was NOT re-executed.",
                approvalId, requestType.Name);
            return Result.Failure(new Error("approval.payload-invalid", "The stored request payload could not be read."));
        }

        _replayContext.IsReplaying = true;
        try
        {
            var response = await _mediator.Send(deserialized, cancellationToken);
            return response is Result result && result.IsFailure ? Result.Failure(result.Error) : Result.Success();
        }
        finally
        {
            _replayContext.IsReplaying = false;
        }
    }

    public async Task<Result<ApprovalRequestDto>> RejectApprovalAsync(Guid approvalId, string comment, Guid reviewerUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.ApprovalRequests.FirstOrDefaultAsync(x => x.Id == approvalId, cancellationToken);
        if (entity is null)
        {
            return Result<ApprovalRequestDto>.Failure(new Error("Approvals.NotFound", "Approval request was not found."));
        }

        var review = await AuthorizeReviewAsync(entity, approving: false, cancellationToken);
        if (review.IsFailure)
        {
            return Result<ApprovalRequestDto>.Failure(review.Error);
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
        // One row per (period, module): locking a month that was unlocked before re-locks that row instead of inserting a second one.
        var periodKey = request.PeriodKey.Trim();
        var moduleCode = request.ModuleCode.Trim();
        var existing = await _dbContext.PeriodLocks.FirstOrDefaultAsync(x => x.PeriodKey == periodKey && x.ModuleCode == moduleCode, cancellationToken);
        if (existing is { IsLocked: true })
        {
            return Result<PeriodLockDto>.Failure(new Error("Periods.Conflict", $"Period {periodKey} is already locked for module {moduleCode}."));
        }

        var entity = existing ?? new PeriodLock(Guid.NewGuid(), periodKey, moduleCode, actorUserId, request.Reason);
        if (existing is null)
        {
            _dbContext.PeriodLocks.Add(entity);
        }
        else
        {
            entity.Relock(actorUserId, request.Reason);
        }

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

    /// <summary>
    /// Who may review a request, decided in this one place. A warehouse transfer is reviewed by the managers of its warehouses under
    /// <see cref="TransferApprovalPolicy"/> (the returned value says whether this approval completes it); anything else needs approvals.review.
    /// SYSTEM_ADMIN may review everything.
    /// </summary>
    private async Task<Result<bool?>> AuthorizeReviewAsync(ApprovalRequest entity, bool approving, CancellationToken cancellationToken)
    {
        var isAdmin = _currentUser.HasRole(RoleCodes.SystemAdministrator);
        var scope = await ScopeAsync(entity.Id, cancellationToken);
        if (scope.Length == 0)
        {
            return isAdmin || _currentUser.HasPermission(PermissionCodes.ApprovalsReview)
                ? Result<bool?>.Success(null)
                : await RefuseAsync(new Error("Authorization.Forbidden", $"Permission '{PermissionCodes.ApprovalsReview}' is required."), cancellationToken);
        }

        var reviewerManaged = await _warehouses.ManagedWarehousesAsync(_currentUser.UserId, cancellationToken);
        if (!approving)
        {
            return TransferApprovalPolicy.CanReject(scope, reviewerManaged, isAdmin)
                ? Result<bool?>.Success(null)
                : await RefuseAsync(TransferApprovalPolicy.NotAManager, cancellationToken);
        }

        var earlier = new List<IReadOnlySet<Guid>>();
        foreach (var decision in entity.Decisions.Where(d => d.IsApproval))
        {
            earlier.Add(await _warehouses.ManagedWarehousesAsync(decision.ReviewerUserId, cancellationToken));
        }

        var outcome = TransferApprovalPolicy.EvaluateApproval(
            scope, await _warehouses.ManagedWarehousesAsync(entity.RequestedByUserId, cancellationToken), earlier, reviewerManaged, isAdmin);
        return outcome.Allowed ? Result<bool?>.Success(outcome.Completes) : await RefuseAsync(outcome.Error!, cancellationToken);
    }

    private async Task<Result<bool?>> RefuseAsync(Error error, CancellationToken cancellationToken)
    {
        await _audit.LogRejectionAsync(
            new RejectionEntry(_currentUser.CorrelationId, _currentUser.UserId, _currentUser.Username, "ReviewApproval", PermissionCodes.ApprovalsReview, error.Message, _currentUser.IpAddress),
            cancellationToken);
        return Result<bool?>.Failure(error);
    }

    /// <summary>The warehouses a held transfer touches (empty for every other kind of request).</summary>
    private async Task<Guid[]> ScopeAsync(Guid approvalId, CancellationToken cancellationToken)
    {
        await using var connection = await _dbConnectionFactory.CreateAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<Guid[]?>(new CommandDefinition(
            "SELECT scope_warehouse_ids FROM approval_requests WHERE id = @approvalId;", new { approvalId }, cancellationToken: cancellationToken)) ?? [];
    }

    private async Task<Guid[]> ApprovalsTouchingAsync(Guid[] warehouses, CancellationToken cancellationToken)
    {
        await using var connection = await _dbConnectionFactory.CreateAsync(cancellationToken);
        return (await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT id FROM approval_requests WHERE scope_warehouse_ids && @warehouses;", new { warehouses }, cancellationToken: cancellationToken))).ToArray();
    }

    /// <summary>Names of the warehouses each held transfer touches, in order (source first).</summary>
    private async Task<Dictionary<Guid, IReadOnlyList<string>>> WarehouseNamesAsync(Guid[] ids, CancellationToken cancellationToken)
    {
        if (ids.Length == 0)
        {
            return [];
        }

        await using var connection = await _dbConnectionFactory.CreateAsync(cancellationToken);
        var rows = await connection.QueryAsync<(Guid Id, string Name, int Position)>(new CommandDefinition(
            """
            SELECT a.id AS Id, l.name AS Name, s.ord::int AS Position
            FROM approval_requests a
            CROSS JOIN LATERAL unnest(a.scope_warehouse_ids) WITH ORDINALITY AS s(warehouse_id, ord)
            INNER JOIN locations l ON l.id = s.warehouse_id
            WHERE a.id = ANY(@ids);
            """,
            new { ids }, cancellationToken: cancellationToken));
        return rows.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.OrderBy(r => r.Position).Select(r => r.Name).ToList());
    }

    private static ApprovalRequestDto ToDto(ApprovalRequest approval) => ToDto(approval, null);

    private static ApprovalRequestDto ToDto(ApprovalRequest approval, IReadOnlyList<string>? warehouses)
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
            approval.Decisions.Select(x => new ApprovalDecisionDto(x.Id, x.ReviewerUserId, x.Status, x.Comment, x.ReviewedAtUtc)).ToArray(),
            warehouses ?? []);
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
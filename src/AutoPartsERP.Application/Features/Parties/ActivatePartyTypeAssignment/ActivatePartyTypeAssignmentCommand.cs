namespace AutoPartsERP.Application.Features.Parties.ActivatePartyTypeAssignment;

public sealed record ActivatePartyTypeAssignmentCommand(Guid PartyId, string TypeCode)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.ApprovalsReview;
    public string AuditModule => "PARTY";
}

public sealed class ActivatePartyTypeAssignmentCommandValidator : AbstractValidator<ActivatePartyTypeAssignmentCommand>
{
    public ActivatePartyTypeAssignmentCommandValidator()
    {
        RuleFor(x => x.PartyId).NotEmpty();
        RuleFor(x => x.TypeCode).NotEmpty();
    }
}

public sealed class ActivatePartyTypeAssignmentCommandHandler : IRequestHandler<ActivatePartyTypeAssignmentCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public ActivatePartyTypeAssignmentCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(ActivatePartyTypeAssignmentCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE party_type_assignments
            SET is_active = TRUE,
                approved_by = @ApprovedBy,
                activated_at = now(),
                deactivated_at = NULL
            WHERE party_id = @PartyId
              AND type_code = @TypeCode;
            """,
            new
            {
                request.PartyId,
                TypeCode = request.TypeCode.Trim().ToUpperInvariant(),
                ApprovedBy = _currentUser.UserId
            },
            transaction,
            cancellationToken: cancellationToken));

        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Party.AssignmentNotFound", "Party type assignment was not found."));
        }

        var outboxMessage = OutboxMessage.Create(
            OutboxEventTypes.PartyTypeActivated,
            "Party",
            request.PartyId,
            new PartyTypeActivatedPayload(
                request.PartyId,
                request.TypeCode.Trim().ToUpperInvariant(),
                _currentUser.UserId),
            _currentUser.CorrelationId);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO outbox_messages (
                id, event_type, aggregate_type, aggregate_id, payload_json, occurred_at,
                processed_at, processing_error, retry_count, correlation_id)
            VALUES (
                @Id, @EventType, @AggregateType, @AggregateId, @PayloadJson, @OccurredAt,
                @ProcessedAt, @ProcessingError, @RetryCount, @CorrelationId);
            """,
            new
            {
                outboxMessage.Id,
                outboxMessage.EventType,
                outboxMessage.AggregateType,
                outboxMessage.AggregateId,
                outboxMessage.PayloadJson,
                outboxMessage.OccurredAt,
                outboxMessage.ProcessedAt,
                outboxMessage.ProcessingError,
                outboxMessage.RetryCount,
                outboxMessage.CorrelationId
            },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(request.PartyId);
    }
}

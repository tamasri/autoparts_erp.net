using Dapper;
using Humanizer;

namespace AutoPartsERP.Application.Features.Warranty.ClaimWarranty;

public sealed record ClaimWarrantyCommand(
    Guid WarrantyRecordId,
    string Description,
    DateOnly ClaimDate,
    string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Warranty.Create;
    public string AuditModule => "WARRANTY";
}

public sealed class ClaimWarrantyCommandValidator : AbstractValidator<ClaimWarrantyCommand>
{
    public ClaimWarrantyCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.WarrantyRecordId).NotEmpty();
        RuleFor(x => x.Description).NotEmpty().MinimumLength(3);
    }
}

public sealed class ClaimWarrantyCommandHandler : IRequestHandler<ClaimWarrantyCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public ClaimWarrantyCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(ClaimWarrantyCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var record = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Status, DateOnly ExpiryDate)>(
            new CommandDefinition(
                "SELECT id AS Id, status AS Status, expiry_date AS ExpiryDate FROM warranty_records WHERE id = @WarrantyRecordId;",
                new { request.WarrantyRecordId },
                transaction,
                cancellationToken: cancellationToken));

        if (record.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Warranty.NotFound", "Warranty record was not found."));
        }

        if (!string.Equals(record.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Warranty.InvalidState", "Only active warranty records can be claimed."));
        }

        if (request.ClaimDate > record.ExpiryDate)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Warranty.Expired", $"Warranty expired منذ {(DateTime.UtcNow - request.ClaimDate.ToDateTime(TimeOnly.MinValue)).Humanize(culture: new System.Globalization.CultureInfo("ar"))}."));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE warranty_records
            SET status = 'CLAIMED',
                claim_date = @ClaimDate,
                claim_description = @Description,
                updated_at = now(),
                updated_by = @UpdatedBy
            WHERE id = @WarrantyRecordId;
            """,
            new { request.WarrantyRecordId, request.ClaimDate, request.Description, UpdatedBy = _currentUser.UserId },
            transaction,
            cancellationToken: cancellationToken));

        var outboxMessage = OutboxMessage.Create(
            OutboxEventTypes.WarrantyClaimCreated,
            "WarrantyRecord",
            request.WarrantyRecordId,
            new WarrantyClaimCreatedPayload(request.WarrantyRecordId, request.ClaimDate),
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
        return Result<Guid>.Success(request.WarrantyRecordId);
    }
}

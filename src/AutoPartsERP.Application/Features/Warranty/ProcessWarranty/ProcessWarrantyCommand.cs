using Dapper;

namespace AutoPartsERP.Application.Features.Warranty.ProcessWarranty;

public sealed record ProcessWarrantyCommand(
    Guid WarrantyRecordId,
    string Resolution,
    Guid? ReplacementSkuId,
    string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Warranty.Process;
    public string AuditModule => "WARRANTY";
}

public sealed class ProcessWarrantyCommandValidator : AbstractValidator<ProcessWarrantyCommand>
{
    public ProcessWarrantyCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.WarrantyRecordId).NotEmpty();
        RuleFor(x => x.Resolution).NotEmpty().MinimumLength(3);
    }
}

public sealed class ProcessWarrantyCommandHandler : IRequestHandler<ProcessWarrantyCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public ProcessWarrantyCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(ProcessWarrantyCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var recordStatus = await connection.ExecuteScalarAsync<string?>(
            new CommandDefinition(
                "SELECT status FROM warranty_records WHERE id = @WarrantyRecordId;",
                new { request.WarrantyRecordId },
                transaction,
                cancellationToken: cancellationToken));

        if (recordStatus is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Warranty.NotFound", "Warranty record was not found."));
        }

        if (!string.Equals(recordStatus, "CLAIMED", StringComparison.OrdinalIgnoreCase))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Warranty.InvalidState", "Only claimed warranty records can be processed."));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE warranty_records
            SET status = 'ACTIVE',
                resolution = @Resolution,
                replacement_sku_id = @ReplacementSkuId,
                processed_by = @ProcessedBy,
                processed_at = now(),
                updated_at = now(),
                updated_by = @ProcessedBy
            WHERE id = @WarrantyRecordId;
            """,
            new { request.WarrantyRecordId, request.Resolution, request.ReplacementSkuId, ProcessedBy = _currentUser.UserId },
            transaction,
            cancellationToken: cancellationToken));

        var outboxMessage = OutboxMessage.Create(
            OutboxEventTypes.WarrantyProcessed,
            "WarrantyRecord",
            request.WarrantyRecordId,
            new WarrantyProcessedPayload(request.WarrantyRecordId, request.ReplacementSkuId, request.Resolution),
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

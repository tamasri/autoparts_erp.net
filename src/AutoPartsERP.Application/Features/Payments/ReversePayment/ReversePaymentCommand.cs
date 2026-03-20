using Dapper;

namespace AutoPartsERP.Application.Features.Payments.ReversePayment;

public sealed record ReversePaymentCommand(
    Guid PaymentId,
    string Reason,
    string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.Payments.WriteOff;
    public string AuditModule => "PAYMENTS";
    public bool RequiresApproval => true;
}

public sealed class ReversePaymentCommandValidator : AbstractValidator<ReversePaymentCommand>
{
    public ReversePaymentCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3);
    }
}

public sealed class ReversePaymentCommandHandler : IRequestHandler<ReversePaymentCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public ReversePaymentCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(ReversePaymentCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE payments
            SET is_reversed = true,
                reversed_at = now(),
                reversed_by = @ReversedBy,
                reversal_reason = @Reason,
                updated_at = now()
            WHERE id = @PaymentId
              AND is_reversed = false;
            """,
            new { request.PaymentId, Reason = request.Reason, ReversedBy = _currentUser.UserId },
            transaction,
            cancellationToken: cancellationToken));

        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Payment.AlreadyReversed", "Payment is already reversed."));
        }

        var outboxMessage = OutboxMessage.Create(
            OutboxEventTypes.PaymentReversed,
            "Payment",
            request.PaymentId,
            new PaymentReversedPayload(request.PaymentId, request.Reason, _currentUser.UserId),
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
        return Result<Guid>.Success(request.PaymentId);
    }
}

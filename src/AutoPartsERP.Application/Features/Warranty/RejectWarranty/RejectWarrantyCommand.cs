using Dapper;

namespace AutoPartsERP.Application.Features.Warranty.RejectWarranty;

public sealed record RejectWarrantyCommand(
    Guid WarrantyRecordId,
    string Reason,
    string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.Warranty.Reject;
    public string AuditModule => "WARRANTY";
    public bool RequiresApproval => true;
}

public sealed class RejectWarrantyCommandValidator : AbstractValidator<RejectWarrantyCommand>
{
    public RejectWarrantyCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.WarrantyRecordId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3);
    }
}

public sealed class RejectWarrantyCommandHandler : IRequestHandler<RejectWarrantyCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public RejectWarrantyCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(RejectWarrantyCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE warranty_records
            SET status = 'REJECTED',
                rejection_reason = @Reason,
                processed_by = @ProcessedBy,
                processed_at = now(),
                updated_at = now(),
                updated_by = @ProcessedBy
            WHERE id = @WarrantyRecordId
              AND status = 'CLAIMED';
            """,
            new { request.WarrantyRecordId, request.Reason, ProcessedBy = _currentUser.UserId },
            cancellationToken: cancellationToken));

        return updated == 0
            ? Result<Guid>.Failure(new Error("Warranty.InvalidState", "Only claimed warranty records can be rejected."))
            : Result<Guid>.Success(request.WarrantyRecordId);
    }
}

namespace AutoPartsERP.Application.Features.Parties.DeactivatePartyTypeAssignment;

public sealed record DeactivatePartyTypeAssignmentCommand(
    Guid PartyId,
    string TypeCode,
    string Reason,
    string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.Party.AssignType;
    public string AuditModule => "PARTY";
    public bool RequiresApproval => true;
}

public sealed class DeactivatePartyTypeAssignmentCommandValidator : AbstractValidator<DeactivatePartyTypeAssignmentCommand>
{
    public DeactivatePartyTypeAssignmentCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.PartyId).NotEmpty();
        RuleFor(x => x.TypeCode).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3);
    }
}

public sealed class DeactivatePartyTypeAssignmentCommandHandler : IRequestHandler<DeactivatePartyTypeAssignmentCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public DeactivatePartyTypeAssignmentCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(DeactivatePartyTypeAssignmentCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE party_type_assignments
            SET is_active = FALSE,
                approved_by = @ApprovedBy,
                deactivated_at = now()
            WHERE party_id = @PartyId
              AND type_code = @TypeCode
              AND is_active = TRUE;
            """,
            new
            {
                request.PartyId,
                TypeCode = request.TypeCode.Trim().ToUpperInvariant(),
                ApprovedBy = _currentUser.UserId
            },
            cancellationToken: cancellationToken));

        return updated == 0
            ? Result<Guid>.Failure(new Error("Party.AssignmentNotFound", "Party type assignment was not found."))
            : Result<Guid>.Success(request.PartyId);
    }
}

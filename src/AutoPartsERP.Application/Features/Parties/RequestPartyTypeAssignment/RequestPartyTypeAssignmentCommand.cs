namespace AutoPartsERP.Application.Features.Parties.RequestPartyTypeAssignment;

public sealed record RequestPartyTypeAssignmentCommand(
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

public sealed class RequestPartyTypeAssignmentCommandValidator : AbstractValidator<RequestPartyTypeAssignmentCommand>
{
    public RequestPartyTypeAssignmentCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.PartyId).NotEmpty();
        RuleFor(x => x.TypeCode)
            .NotEmpty()
            .Must(code => PartyTypeCodes.All.Contains(code.Trim().ToUpperInvariant(), StringComparer.Ordinal));
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3);
    }
}

public sealed class RequestPartyTypeAssignmentCommandHandler : IRequestHandler<RequestPartyTypeAssignmentCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public RequestPartyTypeAssignmentCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(RequestPartyTypeAssignmentCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var partyExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM parties WHERE id = @PartyId);",
            new { request.PartyId },
            cancellationToken: cancellationToken));

        if (!partyExists)
        {
            return Result<Guid>.Failure(new Error("Party.NotFound", "Party was not found."));
        }

        var typeCode = request.TypeCode.Trim().ToUpperInvariant();
        var assignmentId = Guid.NewGuid();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO party_type_assignments (
                id, party_id, type_code, is_active, requested_by, approval_id, created_at)
            VALUES (
                @Id, @PartyId, @TypeCode, FALSE, @RequestedBy, NULL, now())
            ON CONFLICT (party_id, type_code) DO UPDATE
            SET is_active = EXCLUDED.is_active,
                requested_by = EXCLUDED.requested_by,
                approval_id = EXCLUDED.approval_id,
                deactivated_at = NULL;
            """,
            new
            {
                Id = assignmentId,
                request.PartyId,
                TypeCode = typeCode,
                RequestedBy = _currentUser.UserId
            },
            cancellationToken: cancellationToken));

        return Result<Guid>.Success(assignmentId);
    }
}

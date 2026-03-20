namespace AutoPartsERP.Application.Features.Parties.UpdateParty;

public sealed record UpdatePartyCommand(Guid PartyId, UpdatePartyRequest Request)
    : IRequest<Result<PartyProfileDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Party.Update;
    public string AuditModule => "PARTY";
}

public sealed class UpdatePartyCommandValidator : AbstractValidator<UpdatePartyCommand>
{
    public UpdatePartyCommandValidator()
    {
        RuleFor(x => x.PartyId).NotEmpty();
        RuleFor(x => x.Request.DisplayName).NotEmpty().Length(2, 200);
        RuleFor(x => x.Request.DisplayNameAr).NotEmpty().Length(2, 200);
    }
}

public sealed class UpdatePartyCommandHandler : IRequestHandler<UpdatePartyCommand, Result<PartyProfileDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public UpdatePartyCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<PartyProfileDto>> Handle(UpdatePartyCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE parties
            SET display_name = @DisplayName,
                display_name_ar = @DisplayNameAr,
                tax_number = @TaxNumber,
                website = @Website,
                notes = @Notes,
                is_active = @IsActive,
                updated_at = now(),
                updated_by = @UpdatedBy
            WHERE id = @PartyId;
            """,
            new
            {
                request.PartyId,
                request.Request.DisplayName,
                request.Request.DisplayNameAr,
                request.Request.TaxNumber,
                request.Request.Website,
                request.Request.Notes,
                request.Request.IsActive,
                UpdatedBy = _currentUser.UserId
            },
            cancellationToken: cancellationToken));

        if (updated == 0)
        {
            return Result<PartyProfileDto>.Failure(new Error("Party.NotFound", "Party was not found."));
        }

        var profile = await connection.QuerySingleAsync<PartyProfileProjection>(new CommandDefinition(
            """
            SELECT
                p.id AS Id,
                p.code AS Code,
                p.display_name AS DisplayName,
                p.display_name_ar AS DisplayNameAr,
                p.tax_number AS TaxNumber,
                p.website AS Website,
                p.notes AS Notes,
                p.is_active AS IsActive,
                p.created_at AS CreatedAt
            FROM parties p
            WHERE p.id = @PartyId;
            """,
            new { request.PartyId },
            cancellationToken: cancellationToken));

        return Result<PartyProfileDto>.Success(new PartyProfileDto(
            profile.Id,
            profile.Code,
            profile.DisplayName,
            profile.DisplayNameAr,
            profile.TaxNumber,
            profile.Website,
            profile.Notes,
            profile.IsActive,
            false,
            false,
            false,
            false,
            Array.Empty<PartyTypeAssignmentDto>(),
            Array.Empty<PartyContactDto>(),
            Array.Empty<PartyAddressDto>(),
            Array.Empty<PartyNoteDto>(),
            profile.CreatedAt));
    }

    private sealed class PartyProfileProjection
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string DisplayNameAr { get; init; } = string.Empty;
        public string? TaxNumber { get; init; }
        public string? Website { get; init; }
        public string? Notes { get; init; }
        public bool IsActive { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }
}

namespace AutoPartsERP.Application.Features.Parties.CreateParty;

public sealed record CreatePartyCommand(CreatePartyRequest Request, string IdempotencyKey)
    : IRequest<Result<PartyProfileDto>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Party.Create;
    public string AuditModule => "PARTY";
}

public sealed class CreatePartyCommandValidator : AbstractValidator<CreatePartyCommand>
{
    public CreatePartyCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Request.DisplayName).NotEmpty().Length(2, 200);
        RuleFor(x => x.Request.DisplayNameAr).NotEmpty().Length(2, 200);
        RuleForEach(x => x.Request.InitialTypeCodes!)
            .NotEmpty()
            .Must(code => PartyTypeCodes.All.Contains(code.Trim().ToUpperInvariant(), StringComparer.Ordinal))
            .When(x => x.Request.InitialTypeCodes is { Count: > 0 });
    }
}

public sealed class CreatePartyCommandHandler : IRequestHandler<CreatePartyCommand, Result<PartyProfileDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreatePartyCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<PartyProfileDto>> Handle(CreatePartyCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var partyCode = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT 'PTY-' || LPAD(nextval('party_code_seq')::text, 4, '0');",
            transaction: transaction,
            cancellationToken: cancellationToken));

        if (string.IsNullOrWhiteSpace(partyCode))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<PartyProfileDto>.Failure(new Error("Party.CodeGenerationFailed", "Party code could not be generated."));
        }

        var partyResult = Party.Create(
            partyCode,
            request.Request.DisplayName,
            request.Request.DisplayNameAr,
            request.Request.TaxNumber,
            _currentUser.UserId);

        if (partyResult.IsFailure || partyResult.Value is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<PartyProfileDto>.Failure(partyResult.Error);
        }

        var party = partyResult.Value;
        party.UpdateProfile(
            request.Request.DisplayName,
            request.Request.DisplayNameAr,
            request.Request.TaxNumber,
            request.Request.Website,
            request.Request.Notes,
            _currentUser.UserId);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO parties (
                id, code, display_name, display_name_ar, tax_number, website, notes, is_active,
                created_at, created_by, updated_at, updated_by)
            VALUES (
                @Id, @Code, @DisplayName, @DisplayNameAr, @TaxNumber, @Website, @Notes, TRUE,
                @CreatedAt, @CreatedBy, @UpdatedAt, @UpdatedBy);
            """,
            new
            {
                party.Id,
                party.Code,
                party.DisplayName,
                party.DisplayNameAr,
                party.TaxNumber,
                party.Website,
                party.Notes,
                CreatedAt = party.CreatedAtUtc,
                CreatedBy = party.CreatedBy,
                UpdatedAt = party.UpdatedAtUtc,
                UpdatedBy = party.UpdatedBy
            },
            transaction,
            cancellationToken: cancellationToken));

        var typeCodes = request.Request.InitialTypeCodes?
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();

        foreach (var typeCode in typeCodes)
        {
            var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS(SELECT 1 FROM party_type_catalog WHERE code = @Code);",
                new { Code = typeCode },
                transaction,
                cancellationToken: cancellationToken));

            if (!exists)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<PartyProfileDto>.Failure(
                    new Error("Party.TypeCodeInvalid", $"Type code '{typeCode}' does not exist."));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO party_type_assignments (
                    id, party_id, type_code, is_active, requested_by, approved_by, activated_at, created_at)
                VALUES (
                    @Id, @PartyId, @TypeCode, TRUE, @RequestedBy, @RequestedBy, now(), now())
                ON CONFLICT (party_id, type_code) DO NOTHING;
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    PartyId = party.Id,
                    TypeCode = typeCode,
                    RequestedBy = _currentUser.UserId
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);

        var showArTab = typeCodes.Contains(PartyTypeCodes.Customer, StringComparer.Ordinal);
        var showApTab = typeCodes.Contains(PartyTypeCodes.Vendor, StringComparer.Ordinal);
        var showHrTab = typeCodes.Contains(PartyTypeCodes.Employee, StringComparer.Ordinal);

        return Result<PartyProfileDto>.Success(new PartyProfileDto(
            party.Id,
            party.Code,
            party.DisplayName,
            party.DisplayNameAr,
            party.TaxNumber,
            party.Website,
            party.Notes,
            party.IsActive,
            showArTab && showApTab,
            showArTab,
            showApTab,
            showHrTab,
            typeCodes.Select(code => new PartyTypeAssignmentDto(
                Guid.Empty,
                code,
                true,
                _currentUser.UserId,
                _currentUser.UserId,
                null,
                DateTimeOffset.UtcNow,
                null,
                DateTimeOffset.UtcNow)).ToArray(),
            Array.Empty<PartyContactDto>(),
            Array.Empty<PartyAddressDto>(),
            Array.Empty<PartyNoteDto>(),
            party.CreatedAtUtc));
    }
}

namespace AutoPartsERP.Application.Features.Parties.GetPartyById;

public sealed record GetPartyByIdQuery(Guid PartyId)
    : IRequest<Result<PartyProfileDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Party.Read;
}

public sealed class GetPartyByIdQueryHandler : IRequestHandler<GetPartyByIdQuery, Result<PartyProfileDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetPartyByIdQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PartyProfileDto>> Handle(GetPartyByIdQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var party = await connection.QuerySingleOrDefaultAsync<PartyRow>(new CommandDefinition(
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

        if (party is null)
        {
            return Result<PartyProfileDto>.Failure(new Error("Party.NotFound", "Party was not found."));
        }

        var assignments = (await connection.QueryAsync<AssignmentRow>(new CommandDefinition(
            """
            SELECT
                a.id AS Id,
                a.type_code AS TypeCode,
                a.is_active AS IsActive,
                a.requested_by AS RequestedBy,
                a.approved_by AS ApprovedBy,
                a.approval_id AS ApprovalId,
                a.activated_at AS ActivatedAt,
                a.deactivated_at AS DeactivatedAt,
                a.created_at AS CreatedAt
            FROM party_type_assignments a
            WHERE a.party_id = @PartyId
            ORDER BY a.created_at DESC;
            """,
            new { request.PartyId },
            cancellationToken: cancellationToken))).ToArray();

        var contacts = (await connection.QueryAsync<ContactRow>(new CommandDefinition(
            """
            SELECT
                id AS Id,
                type AS Type,
                value AS Value,
                label AS Label,
                is_primary AS IsPrimary,
                created_at AS CreatedAt
            FROM party_contacts
            WHERE party_id = @PartyId
            ORDER BY created_at DESC;
            """,
            new { request.PartyId },
            cancellationToken: cancellationToken))).ToArray();

        var addresses = (await connection.QueryAsync<AddressRow>(new CommandDefinition(
            """
            SELECT
                id AS Id,
                type AS Type,
                line1 AS Line1,
                line2 AS Line2,
                city AS City,
                region AS Region,
                country AS Country,
                is_default AS IsDefault,
                created_at AS CreatedAt
            FROM party_addresses
            WHERE party_id = @PartyId
            ORDER BY created_at DESC;
            """,
            new { request.PartyId },
            cancellationToken: cancellationToken))).ToArray();

        var notes = (await connection.QueryAsync<NoteRow>(new CommandDefinition(
            """
            SELECT
                id AS Id,
                content AS Content,
                is_pinned AS IsPinned,
                created_at AS CreatedAt
            FROM party_notes
            WHERE party_id = @PartyId
            ORDER BY created_at DESC;
            """,
            new { request.PartyId },
            cancellationToken: cancellationToken))).ToArray();

        var activeTypeCodes = assignments
            .Where(x => x.IsActive)
            .Select(x => x.TypeCode)
            .ToHashSet(StringComparer.Ordinal);

        var tabs = await connection.QuerySingleOrDefaultAsync<TabFlags>(new CommandDefinition(
            """
            SELECT
                BOOL_OR(c.opens_ar) AS ShowArTab,
                BOOL_OR(c.opens_ap) AS ShowApTab,
                BOOL_OR(c.opens_hr) AS ShowHrTab
            FROM party_type_assignments a
            INNER JOIN party_type_catalog c ON c.code = a.type_code
            WHERE a.party_id = @PartyId
              AND a.is_active = TRUE;
            """,
            new { request.PartyId },
            cancellationToken: cancellationToken)) ?? new TabFlags();

        var dto = new PartyProfileDto(
            party.Id,
            party.Code,
            party.DisplayName,
            party.DisplayNameAr,
            party.TaxNumber,
            party.Website,
            party.Notes,
            party.IsActive,
            activeTypeCodes.Contains(PartyTypeCodes.Customer) && activeTypeCodes.Contains(PartyTypeCodes.Vendor),
            tabs.ShowArTab,
            tabs.ShowApTab,
            tabs.ShowHrTab,
            assignments.Select(x => new PartyTypeAssignmentDto(
                x.Id,
                x.TypeCode,
                x.IsActive,
                x.RequestedBy,
                x.ApprovedBy,
                x.ApprovalId,
                x.ActivatedAt,
                x.DeactivatedAt,
                x.CreatedAt)).ToArray(),
            contacts.Select(x => new PartyContactDto(
                x.Id,
                x.Type,
                x.Value,
                x.Label,
                x.IsPrimary,
                x.CreatedAt)).ToArray(),
            addresses.Select(x => new PartyAddressDto(
                x.Id,
                x.Type,
                x.Line1,
                x.Line2,
                x.City,
                x.Region,
                x.Country,
                x.IsDefault,
                x.CreatedAt)).ToArray(),
            notes.Select(x => new PartyNoteDto(
                x.Id,
                x.Content,
                x.IsPinned,
                x.CreatedAt)).ToArray(),
            party.CreatedAt);

        return Result<PartyProfileDto>.Success(dto);
    }

    private sealed class PartyRow
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

    private sealed class AssignmentRow
    {
        public Guid Id { get; init; }
        public string TypeCode { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public Guid RequestedBy { get; init; }
        public Guid? ApprovedBy { get; init; }
        public Guid? ApprovalId { get; init; }
        public DateTimeOffset? ActivatedAt { get; init; }
        public DateTimeOffset? DeactivatedAt { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    private sealed class ContactRow
    {
        public Guid Id { get; init; }
        public string Type { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
        public string? Label { get; init; }
        public bool IsPrimary { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    private sealed class AddressRow
    {
        public Guid Id { get; init; }
        public string Type { get; init; } = string.Empty;
        public string Line1 { get; init; } = string.Empty;
        public string? Line2 { get; init; }
        public string? City { get; init; }
        public string? Region { get; init; }
        public string Country { get; init; } = string.Empty;
        public bool IsDefault { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    private sealed class NoteRow
    {
        public Guid Id { get; init; }
        public string Content { get; init; } = string.Empty;
        public bool IsPinned { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    private sealed class TabFlags
    {
        public bool ShowArTab { get; init; }
        public bool ShowApTab { get; init; }
        public bool ShowHrTab { get; init; }
    }
}

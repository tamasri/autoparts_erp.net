using AutoPartsERP.Contracts.Accounting;

namespace AutoPartsERP.Application.Features.Accounting;

// Entry types are what a manual entry is filed under (receipt, payment, contra, journal, ...). The business can add its own, each with its
// own name and number prefix; the kind it belongs to decides how the entry is booked in ERPNext.

public sealed record GetEntryTypesQuery(bool IncludeInactive) : IRequest<Result<IReadOnlyList<EntryTypeDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetEntryTypesQueryHandler : IRequestHandler<GetEntryTypesQuery, Result<IReadOnlyList<EntryTypeDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetEntryTypesQueryHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result<IReadOnlyList<EntryTypeDto>>> Handle(GetEntryTypesQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<EntryTypeDto>(new CommandDefinition(
            """
            SELECT id AS Id, code AS Code, name_ar AS NameAr, kind AS Kind, prefix AS Prefix, description AS Description, is_system AS IsSystem, is_active AS IsActive
            FROM entry_types
            WHERE (@IncludeInactive OR is_active)
            ORDER BY is_system DESC, created_at, name_ar;
            """,
            new { request.IncludeInactive }, cancellationToken: cancellationToken));
        return Result<IReadOnlyList<EntryTypeDto>>.Success(rows.ToList());
    }
}

/// <summary>Creates a new entry type (<c>Id</c> null) or edits one. The built-in types keep their kind and prefix; they can be renamed or switched off.</summary>
public sealed record SaveEntryTypeCommand(Guid? Id, SaveEntryTypeRequest Request) : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.ManageAccounts;
    public string AuditModule => "ACCOUNTING";
}

public sealed class SaveEntryTypeCommandValidator : AbstractValidator<SaveEntryTypeCommand>
{
    public SaveEntryTypeCommandValidator()
    {
        RuleFor(x => x.Request.NameAr).NotEmpty().Length(2, 60);
        RuleFor(x => x.Request.Kind).Must(k => EntryKinds.All.Contains(k)).WithMessage("Unknown entry kind.");
        RuleFor(x => x.Request.Prefix).Matches("^[A-Za-z]{1,6}$").WithMessage("The prefix is 1 to 6 letters.");
        RuleFor(x => x.Request.Description).MaximumLength(200);
    }
}

public sealed class SaveEntryTypeCommandHandler : IRequestHandler<SaveEntryTypeCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SaveEntryTypeCommandHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result<Guid>> Handle(SaveEntryTypeCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var prefix = request.Prefix.Trim().ToUpperInvariant();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var clash = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM entry_types WHERE prefix = @prefix AND id IS DISTINCT FROM @Id);", new { prefix, command.Id }, cancellationToken: cancellationToken));
        if (clash)
        {
            return Result<Guid>.Failure(new Error("EntryType.PrefixConflict", "Another entry type already uses this prefix."));
        }

        if (command.Id is null)
        {
            var id = Guid.NewGuid();
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO entry_types (id, code, name_ar, kind, prefix, description) VALUES (@id, 'X_' || @prefix, @NameAr, @Kind, @prefix, @Description);",
                new { id, prefix, request.NameAr, request.Kind, request.Description }, cancellationToken: cancellationToken));
            return Result<Guid>.Success(id);
        }

        var current = await connection.QuerySingleOrDefaultAsync<(bool IsSystem, string Kind, string Prefix)>(new CommandDefinition(
            "SELECT is_system AS IsSystem, kind AS Kind, prefix AS Prefix FROM entry_types WHERE id = @Id;", new { command.Id }, cancellationToken: cancellationToken));
        if (current.Kind is null)
        {
            return Result<Guid>.Failure(new Error("EntryType.NotFound", "Entry type was not found."));
        }

        if (current.IsSystem && (current.Kind != request.Kind || current.Prefix != prefix))
        {
            return Result<Guid>.Failure(new Error("EntryType.SystemLocked", "A built-in entry type keeps its kind and prefix."));
        }

        // Numbers already issued carry the old prefix, so a used type keeps it too.
        var used = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM journal_entries WHERE entry_type_id = @Id);", new { command.Id }, cancellationToken: cancellationToken));
        if (used && (current.Kind != request.Kind || current.Prefix != prefix))
        {
            return Result<Guid>.Failure(new Error("EntryType.InUse", "Entries already use this type, so its kind and prefix cannot change."));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE entry_types SET name_ar = @NameAr, kind = @Kind, prefix = @prefix, description = @Description, is_active = @IsActive WHERE id = @Id;",
            new { command.Id, request.NameAr, request.Kind, prefix, request.Description, request.IsActive }, cancellationToken: cancellationToken));
        return Result<Guid>.Success(command.Id.Value);
    }
}

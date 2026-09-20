using AutoPartsERP.Contracts.Accounting;

namespace AutoPartsERP.Application.Features.Accounting;

// Tags label entries and ledger vouchers ("audit 2026", "to review", a project name) so they can be found and filtered later.

public sealed record GetTagsQuery : IRequest<Result<IReadOnlyList<TagDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.Read;
}

public sealed class GetTagsQueryHandler : IRequestHandler<GetTagsQuery, Result<IReadOnlyList<TagDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetTagsQueryHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result<IReadOnlyList<TagDto>>> Handle(GetTagsQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<TagDto>(new CommandDefinition(
            "SELECT id AS Id, name AS Name, color AS Color FROM accounting_tags ORDER BY name;", cancellationToken: cancellationToken));
        return Result<IReadOnlyList<TagDto>>.Success(rows.ToList());
    }
}

public sealed record SaveTagCommand(Guid? Id, SaveTagRequest Request) : IRequest<Result<TagDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.PostEntries;
    public string AuditModule => "ACCOUNTING";
}

public sealed class SaveTagCommandValidator : AbstractValidator<SaveTagCommand>
{
    public SaveTagCommandValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().Length(1, 40);
        RuleFor(x => x.Request.Color).Matches("^#[0-9a-fA-F]{6}$").When(x => !string.IsNullOrEmpty(x.Request.Color)).WithMessage("The colour is like #5c54ff.");
    }
}

public sealed class SaveTagCommandHandler : IRequestHandler<SaveTagCommand, Result<TagDto>>
{
    private const string DefaultColor = "#5c54ff";

    private readonly IDbConnectionFactory _connectionFactory;

    public SaveTagCommandHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result<TagDto>> Handle(SaveTagCommand command, CancellationToken cancellationToken)
    {
        var name = command.Request.Name.Trim();
        var color = string.IsNullOrEmpty(command.Request.Color) ? DefaultColor : command.Request.Color;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var clash = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM accounting_tags WHERE lower(name) = lower(@name) AND id IS DISTINCT FROM @Id);", new { name, command.Id }, cancellationToken: cancellationToken));
        if (clash)
        {
            return Result<TagDto>.Failure(new Error("Tag.Conflict", "A tag with this name already exists."));
        }

        var id = command.Id ?? Guid.NewGuid();
        var saved = command.Id is null
            ? await connection.ExecuteAsync(new CommandDefinition("INSERT INTO accounting_tags (id, name, color) VALUES (@id, @name, @color);", new { id, name, color }, cancellationToken: cancellationToken))
            : await connection.ExecuteAsync(new CommandDefinition("UPDATE accounting_tags SET name = @name, color = @color WHERE id = @id;", new { id, name, color }, cancellationToken: cancellationToken));
        return saved == 0 ? Result<TagDto>.Failure(new Error("Tag.NotFound", "Tag was not found.")) : Result<TagDto>.Success(new TagDto(id, name, color));
    }
}

public sealed record DeleteTagCommand(Guid Id) : IRequest<Result>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.PostEntries;
    public string AuditModule => "ACCOUNTING";
}

public sealed class DeleteTagCommandHandler : IRequestHandler<DeleteTagCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DeleteTagCommandHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result> Handle(DeleteTagCommand command, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var removed = await connection.ExecuteAsync(new CommandDefinition("DELETE FROM accounting_tags WHERE id = @Id;", new { command.Id }, cancellationToken: cancellationToken));
        return removed == 0 ? Result.Failure(new Error("Tag.NotFound", "Tag was not found.")) : Result.Success();
    }
}

/// <summary>Puts a tag on a manual entry or an ERPNext voucher, or takes it off again.</summary>
public sealed record SetTagCommand(Guid TagId, TagTargetRequest Target, bool Attach) : IRequest<Result>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Accounting.PostEntries;
    public string AuditModule => "ACCOUNTING";
}

public sealed class SetTagCommandValidator : AbstractValidator<SetTagCommand>
{
    public SetTagCommandValidator()
    {
        RuleFor(x => x.Target.TargetType).Must(t => t is TagResolver.JournalEntryTarget or TagResolver.ErpNextTarget).WithMessage("Unknown tag target.");
        RuleFor(x => x.Target.TargetKey).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Target).Must(t => t.TargetType != TagResolver.JournalEntryTarget || Guid.TryParse(t.TargetKey, out _)).WithMessage("An entry key is its id.");
        RuleFor(x => x.Target).Must(t => t.TargetType != TagResolver.ErpNextTarget || (t.TargetKey.Contains('|') && !t.TargetKey.StartsWith('|') && !t.TargetKey.EndsWith('|')))
            .WithMessage("A voucher key is '<voucher type>|<voucher number>'.");
    }
}

public sealed class SetTagCommandHandler : IRequestHandler<SetTagCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SetTagCommandHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result> Handle(SetTagCommand command, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        if (!await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS (SELECT 1 FROM accounting_tags WHERE id = @TagId);", new { command.TagId }, cancellationToken: cancellationToken)))
        {
            return Result.Failure(new Error("Tag.NotFound", "Tag was not found."));
        }

        var args = new { command.TagId, command.Target.TargetType, command.Target.TargetKey };
        if (command.Target.TargetType == TagResolver.JournalEntryTarget
            && !await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS (SELECT 1 FROM journal_entries WHERE id = @Id);", new { Id = Guid.Parse(command.Target.TargetKey) }, cancellationToken: cancellationToken)))
        {
            return Result.Failure(new Error("JournalEntry.NotFound", "Entry was not found."));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            command.Attach
                ? "INSERT INTO accounting_tag_links (tag_id, target_type, target_key) VALUES (@TagId, @TargetType, @TargetKey) ON CONFLICT DO NOTHING;"
                : "DELETE FROM accounting_tag_links WHERE tag_id = @TagId AND target_type = @TargetType AND target_key = @TargetKey;",
            args, cancellationToken: cancellationToken));
        return Result.Success();
    }
}

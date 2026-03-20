using System.Text.RegularExpressions;

namespace AutoPartsERP.Application.Features.Catalog.CreateCategory;

public sealed record CreateCategoryCommand(CreateCategoryRequest Request, string IdempotencyKey)
    : IRequest<Result<CategoryDto>>, IAuthorizedRequest, IIdempotentRequest
{
    public string RequiredPermission => PermissionCodes.Catalog.Write;
}

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.NameAr).MaximumLength(200);
    }
}

public sealed class CreateCategoryCommandHandler : IRequestHandler<CreateCategoryCommand, Result<CategoryDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreateCategoryCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<CategoryDto>> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        string path;
        int depth;

        if (request.Request.ParentId is null)
        {
            path = Slugify(request.Request.Name);
            depth = 0;
        }
        else
        {
            var parent = await GetParentAsync(connection, request.Request.ParentId.Value, cancellationToken);
            if (parent is null)
            {
                return Result<CategoryDto>.Failure(new Error("Category.ParentNotFound", "Parent category was not found."));
            }

            var parentValue = parent.Value;
            path = $"{parentValue.Path}.{Slugify(request.Request.Name)}";
            depth = parentValue.Depth + 1;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO categories (id, path, name, name_ar, parent_id, depth, is_active, created_by)
            VALUES (@Id, @Path, @Name, @NameAr, @ParentId, @Depth, TRUE, @CreatedBy)
            RETURNING id, path, name, name_ar, parent_id, depth, is_active;
            """;

        AddParameter(command, "Id", Guid.NewGuid());
        AddParameter(command, "Path", path);
        AddParameter(command, "Name", request.Request.Name.Trim());
        AddParameter(command, "NameAr", (object?)request.Request.NameAr?.Trim() ?? DBNull.Value);
        AddParameter(command, "ParentId", (object?)request.Request.ParentId ?? DBNull.Value);
        AddParameter(command, "Depth", depth);
        AddParameter(command, "CreatedBy", _currentUser.UserId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result<CategoryDto>.Failure(new Error("Category.CreateFailed", "Category could not be created."));
        }

        return Result<CategoryDto>.Success(MapCategory(reader));
    }

    private static async Task<(string Path, int Depth)?> GetParentAsync(DbConnection connection, Guid parentId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT path::text, depth FROM categories WHERE id = @Id;";
        AddParameter(command, "Id", parentId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (reader.GetString(0), reader.GetInt32(1));
    }

    private static string Slugify(string value)
    {
        var slug = value.Trim().ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9]+", "_");
        slug = Regex.Replace(slug, "_{2,}", "_").Trim('_');
        return string.IsNullOrWhiteSpace(slug) ? "category" : slug;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static CategoryDto MapCategory(DbDataReader reader)
    {
        return new CategoryDto(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("path")),
            reader.GetString(reader.GetOrdinal("name")),
            reader.IsDBNull(reader.GetOrdinal("name_ar")) ? null : reader.GetString(reader.GetOrdinal("name_ar")),
            reader.IsDBNull(reader.GetOrdinal("parent_id")) ? null : reader.GetGuid(reader.GetOrdinal("parent_id")),
            reader.GetInt32(reader.GetOrdinal("depth")),
            reader.GetBoolean(reader.GetOrdinal("is_active")));
    }
}

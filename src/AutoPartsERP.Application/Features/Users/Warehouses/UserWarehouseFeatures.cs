namespace AutoPartsERP.Application.Features.Users.Warehouses;

// The warehouses a user works in and manages. Only administrators set them (users.manage-roles), the change is governed like a role change,
// and nobody sets their own.

public sealed record GetUserWarehousesQuery(Guid UserId) : IRequest<Result<IReadOnlyList<UserWarehouseDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.UsersRead;
}

public sealed class GetUserWarehousesQueryHandler : IRequestHandler<GetUserWarehousesQuery, Result<IReadOnlyList<UserWarehouseDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetUserWarehousesQueryHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result<IReadOnlyList<UserWarehouseDto>>> Handle(GetUserWarehousesQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<UserWarehouseDto>(new CommandDefinition(
            """
            SELECT l.id AS WarehouseId, l.code AS Code, l.name AS Name, l.type AS Type,
                   (uw.user_id IS NOT NULL) AS Assigned, COALESCE(uw.is_manager, FALSE) AS IsManager
            FROM locations l
            LEFT JOIN user_warehouses uw ON uw.warehouse_id = l.id AND uw.user_id = @UserId
            WHERE l.parent_id IS NULL AND l.is_active
            ORDER BY l.name;
            """,
            new { request.UserId }, cancellationToken: cancellationToken));
        return Result<IReadOnlyList<UserWarehouseDto>>.Success(rows.ToList());
    }
}

public sealed record SetUserWarehousesCommand(Guid UserId, SetUserWarehousesRequest Request)
    : IRequest<Result<IReadOnlyList<UserWarehouseDto>>>, IAuthorizedRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.UsersManageRoles;
    public string AuditModule => "USERS";
    public bool RequiresApproval => true;
}

public sealed class SetUserWarehousesCommandValidator : AbstractValidator<SetUserWarehousesCommand>
{
    public SetUserWarehousesCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Request.Warehouses).NotNull();
        RuleFor(x => x.Request.Warehouses).Must(w => w.Select(x => x.WarehouseId).Distinct().Count() == w.Count)
            .When(x => x.Request.Warehouses is not null).WithMessage("A warehouse is listed twice.");
    }
}

public sealed class SetUserWarehousesCommandHandler : IRequestHandler<SetUserWarehousesCommand, Result<IReadOnlyList<UserWarehouseDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly ISender _sender;

    public SetUserWarehousesCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, ISender sender)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _sender = sender;
    }

    public async Task<Result<IReadOnlyList<UserWarehouseDto>>> Handle(SetUserWarehousesCommand command, CancellationToken cancellationToken)
    {
        if (command.UserId == _currentUser.UserId)
        {
            return Result<IReadOnlyList<UserWarehouseDto>>.Failure(new Error("Users.SelfChange", "You cannot change your own warehouses; ask another administrator."));
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        if (!await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS (SELECT 1 FROM asp_net_users WHERE id = @UserId);", new { command.UserId }, cancellationToken: cancellationToken)))
        {
            return Result<IReadOnlyList<UserWarehouseDto>>.Failure(new Error("Users.NotFound", "User was not found."));
        }

        var ids = command.Request.Warehouses.Select(w => w.WarehouseId).ToArray();
        var valid = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM locations WHERE id = ANY(@ids) AND parent_id IS NULL AND is_active;", new { ids }, cancellationToken: cancellationToken));
        if (valid != ids.Length)
        {
            return Result<IReadOnlyList<UserWarehouseDto>>.Failure(new Error("Users.WarehouseNotFound", "Only active top-level warehouses can be assigned."));
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM user_warehouses WHERE user_id = @UserId;", new { command.UserId }, transaction, cancellationToken: cancellationToken));
        foreach (var warehouse in command.Request.Warehouses)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO user_warehouses (user_id, warehouse_id, is_manager, assigned_by) VALUES (@UserId, @WarehouseId, @IsManager, @By);",
                new { command.UserId, warehouse.WarehouseId, warehouse.IsManager, By = _currentUser.UserId }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return await _sender.Send(new GetUserWarehousesQuery(command.UserId), cancellationToken);
    }
}

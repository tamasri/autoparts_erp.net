namespace AutoPartsERP.Application.Features.Roles.GetAllPermissions;

public sealed record GetAllPermissionsQuery()
    : IRequest<Result<IReadOnlyCollection<string>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.RolesRead;
}

public sealed class GetAllPermissionsQueryValidator : AbstractValidator<GetAllPermissionsQuery>
{
}

public sealed class GetAllPermissionsQueryHandler : IRequestHandler<GetAllPermissionsQuery, Result<IReadOnlyCollection<string>>>
{
    private readonly IRoleService _roleService;

    public GetAllPermissionsQueryHandler(IRoleService roleService)
    {
        _roleService = roleService;
    }

    public async Task<Result<IReadOnlyCollection<string>>> Handle(GetAllPermissionsQuery request, CancellationToken cancellationToken)
    {
        var fromService = await _roleService.GetAllPermissionsAsync(cancellationToken);
        if (fromService.IsSuccess)
        {
            return fromService;
        }

        return Result<IReadOnlyCollection<string>>.Success(PermissionCodes.All);
    }
}
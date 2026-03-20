namespace AutoPartsERP.Application.Features.Roles.GetRoles;

public sealed record GetRolesQuery()
    : IRequest<Result<IReadOnlyCollection<RoleSummaryDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.RolesRead;
}

public sealed class GetRolesQueryValidator : AbstractValidator<GetRolesQuery>
{
}

public sealed class GetRolesQueryHandler : IRequestHandler<GetRolesQuery, Result<IReadOnlyCollection<RoleSummaryDto>>>
{
    private readonly IRoleService _roleService;

    public GetRolesQueryHandler(IRoleService roleService)
    {
        _roleService = roleService;
    }

    public async Task<Result<IReadOnlyCollection<RoleSummaryDto>>> Handle(GetRolesQuery request, CancellationToken cancellationToken)
    {
        return await _roleService.GetRolesAsync(cancellationToken);
    }
}
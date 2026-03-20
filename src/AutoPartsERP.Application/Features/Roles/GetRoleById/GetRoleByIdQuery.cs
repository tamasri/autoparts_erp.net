namespace AutoPartsERP.Application.Features.Roles.GetRoleById;

public sealed record GetRoleByIdQuery(Guid RoleId)
    : IRequest<Result<RoleSummaryDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.RolesRead;
}

public sealed class GetRoleByIdQueryValidator : AbstractValidator<GetRoleByIdQuery>
{
    public GetRoleByIdQueryValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public sealed class GetRoleByIdQueryHandler : IRequestHandler<GetRoleByIdQuery, Result<RoleSummaryDto>>
{
    private readonly IRoleService _roleService;

    public GetRoleByIdQueryHandler(IRoleService roleService)
    {
        _roleService = roleService;
    }

    public async Task<Result<RoleSummaryDto>> Handle(GetRoleByIdQuery request, CancellationToken cancellationToken)
    {
        return await _roleService.GetRoleByIdAsync(request.RoleId, cancellationToken);
    }
}
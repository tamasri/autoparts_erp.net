namespace AutoPartsERP.Application.Features.Roles.GrantPermission;

public sealed record GrantPermissionToRoleCommand(Guid RoleId, string PermissionCode)
    : IRequest<Result<RoleSummaryDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.RolesWrite;
    public string AuditModule => "ROLES";
}

public sealed class GrantPermissionToRoleCommandValidator : AbstractValidator<GrantPermissionToRoleCommand>
{
    public GrantPermissionToRoleCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.PermissionCode).NotEmpty().MaximumLength(200);
    }
}

public sealed class GrantPermissionToRoleCommandHandler : IRequestHandler<GrantPermissionToRoleCommand, Result<RoleSummaryDto>>
{
    private readonly IRoleService _roleService;

    public GrantPermissionToRoleCommandHandler(IRoleService roleService)
    {
        _roleService = roleService;
    }

    public async Task<Result<RoleSummaryDto>> Handle(GrantPermissionToRoleCommand request, CancellationToken cancellationToken)
    {
        return await _roleService.GrantPermissionAsync(request.RoleId, request.PermissionCode, cancellationToken);
    }
}
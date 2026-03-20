namespace AutoPartsERP.Application.Features.Roles.RevokePermission;

public sealed record RevokePermissionFromRoleCommand(Guid RoleId, string PermissionCode)
    : IRequest<Result<RoleSummaryDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.RolesWrite;
    public string AuditModule => "ROLES";
}

public sealed class RevokePermissionFromRoleCommandValidator : AbstractValidator<RevokePermissionFromRoleCommand>
{
    public RevokePermissionFromRoleCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.PermissionCode).NotEmpty().MaximumLength(200);
    }
}

public sealed class RevokePermissionFromRoleCommandHandler : IRequestHandler<RevokePermissionFromRoleCommand, Result<RoleSummaryDto>>
{
    private readonly IRoleService _roleService;

    public RevokePermissionFromRoleCommandHandler(IRoleService roleService)
    {
        _roleService = roleService;
    }

    public async Task<Result<RoleSummaryDto>> Handle(RevokePermissionFromRoleCommand request, CancellationToken cancellationToken)
    {
        return await _roleService.RevokePermissionAsync(request.RoleId, request.PermissionCode, cancellationToken);
    }
}
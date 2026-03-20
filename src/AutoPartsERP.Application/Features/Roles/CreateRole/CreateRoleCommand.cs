namespace AutoPartsERP.Application.Features.Roles.CreateRole;

public sealed record CreateRoleCommand(CreateRoleRequest Request, string IdempotencyKey)
    : IRequest<Result<RoleSummaryDto>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.RolesWrite;
    public string AuditModule => "ROLES";
}

public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.Description).NotEmpty().MaximumLength(500);
    }
}

public sealed class CreateRoleCommandHandler : IRequestHandler<CreateRoleCommand, Result<RoleSummaryDto>>
{
    private readonly IRoleService _roleService;

    public CreateRoleCommandHandler(IRoleService roleService)
    {
        _roleService = roleService;
    }

    public async Task<Result<RoleSummaryDto>> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        return await _roleService.CreateRoleAsync(request.Request, cancellationToken);
    }
}
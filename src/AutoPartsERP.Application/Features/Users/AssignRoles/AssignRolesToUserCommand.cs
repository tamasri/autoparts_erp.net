namespace AutoPartsERP.Application.Features.Users.AssignRoles;

public sealed record AssignRolesToUserCommand(Guid UserId, AssignUserRolesRequest Request, DateTimeOffset? ExpiresAt)
    : IRequest<Result<UserDetailsDto>>, IAuthorizedRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.UsersManageRoles;
    public string AuditModule => "USERS";

    // Role assignment is a direct privilege-escalation path (can grant SYSTEM_ADMIN); requires a second approver.
    public bool RequiresApproval => true;
}

public sealed class AssignRolesToUserCommandValidator : AbstractValidator<AssignRolesToUserCommand>
{
    public AssignRolesToUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Request.RoleIds).NotNull().Must(x => x.Count > 0);
    }
}

public sealed class AssignRolesToUserCommandHandler : IRequestHandler<AssignRolesToUserCommand, Result<UserDetailsDto>>
{
    private readonly IUserService _userService;

    public AssignRolesToUserCommandHandler(IUserService userService)
    {
        _userService = userService;
    }

    public async Task<Result<UserDetailsDto>> Handle(AssignRolesToUserCommand request, CancellationToken cancellationToken)
    {
        return await _userService.AssignRolesAsync(request.UserId, request.Request, cancellationToken);
    }
}
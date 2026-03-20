namespace AutoPartsERP.Application.Features.Users.AssignRoles;

public sealed record AssignRolesToUserCommand(Guid UserId, AssignUserRolesRequest Request, DateTimeOffset? ExpiresAt)
    : IRequest<Result<UserDetailsDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.UsersManageRoles;
    public string AuditModule => "USERS";
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
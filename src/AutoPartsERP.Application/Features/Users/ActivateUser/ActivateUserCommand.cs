namespace AutoPartsERP.Application.Features.Users.ActivateUser;

/// <summary>Lets a deactivated (or locked-out) user sign in again.</summary>
public sealed record ActivateUserCommand(Guid UserId) : IRequest<Result<UserDetailsDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.UsersWrite;
    public string AuditModule => "USERS";
}

public sealed class ActivateUserCommandHandler : IRequestHandler<ActivateUserCommand, Result<UserDetailsDto>>
{
    private readonly IUserService _userService;

    public ActivateUserCommandHandler(IUserService userService) { _userService = userService; }

    public Task<Result<UserDetailsDto>> Handle(ActivateUserCommand request, CancellationToken cancellationToken) =>
        _userService.ActivateUserAsync(request.UserId, cancellationToken);
}

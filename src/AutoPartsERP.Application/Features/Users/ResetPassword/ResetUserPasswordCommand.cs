namespace AutoPartsERP.Application.Features.Users.ResetPassword;

/// <summary>
/// An administrator sets a new password for a user. Deliberately not a governed (maker-checker) request: an approval stores the request as
/// plain JSON, and that would keep the new password in the approvals table.
/// </summary>
public sealed record ResetUserPasswordCommand(Guid UserId, string NewPassword) : IRequest<Result<UserDetailsDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.UsersWrite;
    public string AuditModule => "USERS";
}

public sealed class ResetUserPasswordCommandValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}

public sealed class ResetUserPasswordCommandHandler : IRequestHandler<ResetUserPasswordCommand, Result<UserDetailsDto>>
{
    private readonly IUserService _userService;

    public ResetUserPasswordCommandHandler(IUserService userService) { _userService = userService; }

    public Task<Result<UserDetailsDto>> Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken) =>
        _userService.ResetPasswordAsync(request.UserId, request.NewPassword, cancellationToken);
}

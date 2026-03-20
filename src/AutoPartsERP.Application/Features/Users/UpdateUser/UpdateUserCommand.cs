namespace AutoPartsERP.Application.Features.Users.UpdateUser;

public sealed record UpdateUserCommand(Guid UserId, UpdateUserRequest Request)
    : IRequest<Result<UserDetailsDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.UsersWrite;
    public string AuditModule => "USERS";
}

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(x => x.Request.FirstName).NotEmpty().Length(2, 100);
        RuleFor(x => x.Request.LastName).NotEmpty().Length(2, 100);
        RuleFor(x => x.Request.RoleIds).NotNull().Must(x => x.Count > 0);
    }
}

public sealed class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, Result<UserDetailsDto>>
{
    private readonly IUserService _userService;

    public UpdateUserCommandHandler(IUserService userService)
    {
        _userService = userService;
    }

    public async Task<Result<UserDetailsDto>> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        return await _userService.UpdateUserAsync(request.UserId, request.Request, cancellationToken);
    }
}
namespace AutoPartsERP.Application.Features.Users.CreateUser;

public sealed record CreateUserCommand(CreateUserRequest Request, string IdempotencyKey)
    : IRequest<Result<UserDetailsDto>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.UsersWrite;
    public string AuditModule => "USERS";
}

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Request.UserName).NotEmpty().Length(3, 50);
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(x => x.Request.FirstName).NotEmpty().Length(2, 100);
        RuleFor(x => x.Request.LastName).NotEmpty().Length(2, 100);
        RuleFor(x => x.Request.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.Request.RoleIds).NotNull().Must(x => x.Count > 0);
    }
}

public sealed class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, Result<UserDetailsDto>>
{
    private readonly IUserService _userService;

    public CreateUserCommandHandler(IUserService userService)
    {
        _userService = userService;
    }

    public async Task<Result<UserDetailsDto>> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        return await _userService.CreateUserAsync(request.Request, cancellationToken);
    }
}
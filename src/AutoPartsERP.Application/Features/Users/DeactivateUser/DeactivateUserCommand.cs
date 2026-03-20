namespace AutoPartsERP.Application.Features.Users.DeactivateUser;

public sealed record DeactivateUserCommand(Guid UserId, string Reason, string? ReasonCode)
    : IRequest<Result<UserDetailsDto>>, IAuthorizedRequest, IMakerCheckerRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.UsersWrite;
    public bool RequiresApproval => true;
    public string AuditModule => "USERS";
}

public sealed class DeactivateUserCommandValidator : AbstractValidator<DeactivateUserCommand>
{
    public DeactivateUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(5);
        RuleFor(x => x.ReasonCode).MaximumLength(100);
    }
}

public sealed class DeactivateUserCommandHandler : IRequestHandler<DeactivateUserCommand, Result<UserDetailsDto>>
{
    private readonly IUserService _userService;

    public DeactivateUserCommandHandler(IUserService userService)
    {
        _userService = userService;
    }

    public async Task<Result<UserDetailsDto>> Handle(DeactivateUserCommand request, CancellationToken cancellationToken)
    {
        return await _userService.DeactivateUserAsync(request.UserId, request.Reason, request.ReasonCode, cancellationToken);
    }
}
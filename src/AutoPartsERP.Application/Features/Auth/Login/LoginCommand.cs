namespace AutoPartsERP.Application.Features.Auth.Login;

public sealed record LoginCommand(LoginRequest Request)
    : IRequest<Result<AuthTokenResponse>>, IAuditableRequest
{
    public string AuditModule => "AUTH";
}

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Request.UserNameOrEmail).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Request.Password).NotEmpty();
    }
}

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, Result<AuthTokenResponse>>
{
    private readonly IAuthService _authService;

    public LoginCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result<AuthTokenResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        return await _authService.LoginAsync(request.Request, cancellationToken);
    }
}
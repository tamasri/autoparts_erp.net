namespace AutoPartsERP.Application.Features.Auth.Logout;

public sealed record LogoutCommand(string RefreshToken)
    : IRequest<Result<bool>>, IAuditableRequest
{
    public string AuditModule => "AUTH";
}

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result<bool>>
{
    private readonly IAuthService _authService;

    public LogoutCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result<bool>> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        return await _authService.LogoutAsync(request.RefreshToken, cancellationToken);
    }
}
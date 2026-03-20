namespace AutoPartsERP.Application.Features.Auth.Refresh;

public sealed record RefreshTokenCommand(string RefreshToken)
    : IRequest<Result<AuthTokenResponse>>, IAuditableRequest
{
    public string AuditModule => "AUTH";
}

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<AuthTokenResponse>>
{
    private readonly IAuthService _authService;

    public RefreshTokenCommandHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result<AuthTokenResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        return await _authService.RefreshTokenAsync(request.RefreshToken, cancellationToken);
    }
}
namespace AutoPartsERP.Application.Features.Auth.GetCurrentUser;

public sealed record GetCurrentUserQuery()
    : IRequest<Result<CurrentUserResponse>>;

public sealed class GetCurrentUserQueryValidator : AbstractValidator<GetCurrentUserQuery>
{
}

public sealed class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, Result<CurrentUserResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IAuthService _authService;

    public GetCurrentUserQueryHandler(ICurrentUser currentUser, IAuthService authService)
    {
        _currentUser = currentUser;
        _authService = authService;
    }

    public async Task<Result<CurrentUserResponse>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Result<CurrentUserResponse>.Failure(new Error("Auth.Unauthorized", "The current user is not authenticated."));
        }

        return await _authService.GetCurrentUserAsync(_currentUser.UserId, cancellationToken);
    }
}
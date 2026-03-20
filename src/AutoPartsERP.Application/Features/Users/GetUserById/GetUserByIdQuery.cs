namespace AutoPartsERP.Application.Features.Users.GetUserById;

public sealed record GetUserByIdQuery(Guid UserId)
    : IRequest<Result<UserDetailsDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.UsersRead;
}

public sealed class GetUserByIdQueryValidator : AbstractValidator<GetUserByIdQuery>
{
    public GetUserByIdQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public sealed class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, Result<UserDetailsDto>>
{
    private readonly IUserService _userService;

    public GetUserByIdQueryHandler(IUserService userService)
    {
        _userService = userService;
    }

    public async Task<Result<UserDetailsDto>> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        return await _userService.GetUserByIdAsync(request.UserId, cancellationToken);
    }
}
namespace AutoPartsERP.Application.Features.Users.GetUsers;

public sealed record GetUsersQuery(int Page, int PageSize, bool? IsActive, string? RoleCode, string? Search)
    : IRequest<Result<PagedResponse<UserSummaryDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.UsersRead;
}

public sealed class GetUsersQueryValidator : AbstractValidator<GetUsersQuery>
{
    public GetUsersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 500);
    }
}

public sealed class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, Result<PagedResponse<UserSummaryDto>>>
{
    private readonly IUserService _userService;

    public GetUsersQueryHandler(IUserService userService)
    {
        _userService = userService;
    }

    public async Task<Result<PagedResponse<UserSummaryDto>>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var filter = new UserListFilter(request.Page, request.PageSize, request.IsActive, request.RoleCode, request.Search);
        return await _userService.GetUsersAsync(filter, cancellationToken);
    }
}
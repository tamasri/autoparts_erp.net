namespace AutoPartsERP.Application.Features.Approvals.GetPendingApprovals;

public sealed record GetPendingApprovalsQuery(int Page, int PageSize)
    : IRequest<Result<PagedResponse<ApprovalRequestDto>>>
{
    // Authorised in IGovernanceService: approvals.read sees every request, a warehouse manager sees the transfers of their warehouses.
}

public sealed class GetPendingApprovalsQueryValidator : AbstractValidator<GetPendingApprovalsQuery>
{
    public GetPendingApprovalsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 500);
    }
}

public sealed class GetPendingApprovalsQueryHandler : IRequestHandler<GetPendingApprovalsQuery, Result<PagedResponse<ApprovalRequestDto>>>
{
    private readonly IGovernanceService _governanceService;
    private readonly ICurrentUser _currentUser;

    public GetPendingApprovalsQueryHandler(IGovernanceService governanceService, ICurrentUser currentUser)
    {
        _governanceService = governanceService;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResponse<ApprovalRequestDto>>> Handle(GetPendingApprovalsQuery request, CancellationToken cancellationToken)
    {
        var filter = new ApprovalListFilter(request.Page, request.PageSize, true, _currentUser.UserId);
        return await _governanceService.GetApprovalsAsync(filter, cancellationToken);
    }
}
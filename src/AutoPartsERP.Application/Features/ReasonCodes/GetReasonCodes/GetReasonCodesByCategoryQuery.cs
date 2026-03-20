namespace AutoPartsERP.Application.Features.ReasonCodes.GetReasonCodes;

public sealed record GetReasonCodesByCategoryQuery(string Category)
    : IRequest<Result<IReadOnlyCollection<ReasonCodeDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.ReasonCodesRead;
}

public sealed class GetReasonCodesByCategoryQueryValidator : AbstractValidator<GetReasonCodesByCategoryQuery>
{
    public GetReasonCodesByCategoryQueryValidator()
    {
        RuleFor(x => x.Category).NotEmpty().MaximumLength(100);
    }
}

public sealed class GetReasonCodesByCategoryQueryHandler : IRequestHandler<GetReasonCodesByCategoryQuery, Result<IReadOnlyCollection<ReasonCodeDto>>>
{
    private readonly IGovernanceService _governanceService;

    public GetReasonCodesByCategoryQueryHandler(IGovernanceService governanceService)
    {
        _governanceService = governanceService;
    }

    public async Task<Result<IReadOnlyCollection<ReasonCodeDto>>> Handle(GetReasonCodesByCategoryQuery request, CancellationToken cancellationToken)
    {
        return await _governanceService.GetReasonCodesAsync(new ReasonCodeFilter(request.Category, true), cancellationToken);
    }
}
namespace AutoPartsERP.Application.Features.Periods.GetPeriodLocks;

public sealed record GetPeriodLocksQuery(int? Year, int? Month)
    : IRequest<Result<IReadOnlyCollection<PeriodLockDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.PeriodLocksRead;
}

public sealed class GetPeriodLocksQueryValidator : AbstractValidator<GetPeriodLocksQuery>
{
    public GetPeriodLocksQueryValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2020, 2100).When(x => x.Year.HasValue);
        RuleFor(x => x.Month).InclusiveBetween(1, 12).When(x => x.Month.HasValue);
    }
}

public sealed class GetPeriodLocksQueryHandler : IRequestHandler<GetPeriodLocksQuery, Result<IReadOnlyCollection<PeriodLockDto>>>
{
    private readonly IGovernanceService _governanceService;

    public GetPeriodLocksQueryHandler(IGovernanceService governanceService)
    {
        _governanceService = governanceService;
    }

    public async Task<Result<IReadOnlyCollection<PeriodLockDto>>> Handle(GetPeriodLocksQuery request, CancellationToken cancellationToken)
    {
        return await _governanceService.GetPeriodLocksAsync(new PeriodLockFilter(request.Year, request.Month, null), cancellationToken);
    }
}
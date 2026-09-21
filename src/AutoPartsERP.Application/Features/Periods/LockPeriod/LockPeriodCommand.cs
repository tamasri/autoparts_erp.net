namespace AutoPartsERP.Application.Features.Periods.LockPeriod;

// Not period-sensitive on purpose: this command manages the lock itself, so a locked period must still be unlockable.
public sealed record LockPeriodCommand(int Year, int Month, string Module, string Reason)
    : IRequest<Result<PeriodLockDto>>, IAuthorizedRequest, IMakerCheckerRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.PeriodLocksWrite;
    public bool RequiresApproval => true;
    public string AuditModule => "PERIODS";
}

public sealed class LockPeriodCommandValidator : AbstractValidator<LockPeriodCommand>
{
    public LockPeriodCommandValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2020, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Module).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(10);
    }
}

public sealed class LockPeriodCommandHandler : IRequestHandler<LockPeriodCommand, Result<PeriodLockDto>>
{
    private readonly IGovernanceService _governanceService;
    private readonly ICurrentUser _currentUser;
    private readonly IPeriodLockService _periodLock;

    public LockPeriodCommandHandler(IGovernanceService governanceService, ICurrentUser currentUser, IPeriodLockService periodLock)
    {
        _governanceService = governanceService;
        _currentUser = currentUser;
        _periodLock = periodLock;
    }

    public async Task<Result<PeriodLockDto>> Handle(LockPeriodCommand request, CancellationToken cancellationToken)
    {
        var periodKey = $"{request.Year:D4}-{request.Month:D2}";
        var lockRequest = new LockPeriodRequest(periodKey, request.Module, request.Reason);
        var result = await _governanceService.LockPeriodAsync(lockRequest, _currentUser.UserId, cancellationToken);
        if (result.IsSuccess)
        {
            await _periodLock.InvalidateCacheAsync(request.Year, request.Month, request.Module, cancellationToken);
        }

        return result;
    }
}
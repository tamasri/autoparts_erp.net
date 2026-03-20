namespace AutoPartsERP.Application.Features.Periods.LockPeriod;

public sealed record LockPeriodCommand(int Year, int Month, string Module, string Reason)
    : IRequest<Result<PeriodLockDto>>, IAuthorizedRequest, IMakerCheckerRequest, IAuditableRequest, IPeriodSensitiveRequest
{
    public string RequiredPermission => PermissionCodes.PeriodLocksWrite;
    public bool RequiresApproval => true;
    public string AuditModule => "PERIODS";
    public DateTimeOffset OperationDate => new DateTimeOffset(Year, Month, 1, 0, 0, 0, TimeSpan.Zero);
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

    public LockPeriodCommandHandler(IGovernanceService governanceService, ICurrentUser currentUser)
    {
        _governanceService = governanceService;
        _currentUser = currentUser;
    }

    public async Task<Result<PeriodLockDto>> Handle(LockPeriodCommand request, CancellationToken cancellationToken)
    {
        var periodKey = $"{request.Year:D4}-{request.Month:D2}";
        var lockRequest = new LockPeriodRequest(periodKey, request.Module, request.Reason);
        return await _governanceService.LockPeriodAsync(lockRequest, _currentUser.UserId, cancellationToken);
    }
}
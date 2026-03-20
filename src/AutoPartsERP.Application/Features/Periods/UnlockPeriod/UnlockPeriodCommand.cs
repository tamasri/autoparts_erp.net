namespace AutoPartsERP.Application.Features.Periods.UnlockPeriod;

public sealed record UnlockPeriodCommand(int Year, int Month, string Module, string Reason)
    : IRequest<Result<PeriodLockDto>>, IAuthorizedRequest, IMakerCheckerRequest, IAuditableRequest, IPeriodSensitiveRequest
{
    public string RequiredPermission => PermissionCodes.PeriodLocksWrite;
    public bool RequiresApproval => true;
    public string AuditModule => "PERIODS";
    public DateTimeOffset OperationDate => new DateTimeOffset(Year, Month, 1, 0, 0, 0, TimeSpan.Zero);
}

public sealed class UnlockPeriodCommandValidator : AbstractValidator<UnlockPeriodCommand>
{
    public UnlockPeriodCommandValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2020, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Module).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3);
    }
}

public sealed class UnlockPeriodCommandHandler : IRequestHandler<UnlockPeriodCommand, Result<PeriodLockDto>>
{
    private readonly IGovernanceService _governanceService;
    private readonly ICurrentUser _currentUser;

    public UnlockPeriodCommandHandler(IGovernanceService governanceService, ICurrentUser currentUser)
    {
        _governanceService = governanceService;
        _currentUser = currentUser;
    }

    public async Task<Result<PeriodLockDto>> Handle(UnlockPeriodCommand request, CancellationToken cancellationToken)
    {
        var filter = new PeriodLockFilter(request.Year, request.Month, request.Module);
        var locks = await _governanceService.GetPeriodLocksAsync(filter, cancellationToken);
        if (locks.IsFailure || locks.Value is null)
        {
            return Result<PeriodLockDto>.Failure(locks.Error);
        }

        var match = locks.Value.FirstOrDefault(x => x.PeriodKey == $"{request.Year:D4}-{request.Month:D2}" && string.Equals(x.ModuleCode, request.Module, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return Result<PeriodLockDto>.Failure(new Error("Periods.NotFound", "No matching period lock exists for the requested period and module."));
        }

        return await _governanceService.UnlockPeriodAsync(match.Id, new UnlockPeriodRequest(request.Reason), _currentUser.UserId, cancellationToken);
    }
}
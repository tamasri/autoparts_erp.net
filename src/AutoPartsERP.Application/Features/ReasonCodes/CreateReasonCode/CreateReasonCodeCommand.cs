namespace AutoPartsERP.Application.Features.ReasonCodes.CreateReasonCode;

public sealed record CreateReasonCodeCommand(
    string Category,
    string Code,
    string Label,
    bool RequiresApproval,
    bool RequiresNotes,
    int MinNotesLength,
    string RiskLevel)
    : IRequest<Result<ReasonCodeDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.ReasonCodesWrite;
    public string AuditModule => "REASON_CODES";
}

public sealed class CreateReasonCodeCommandValidator : AbstractValidator<CreateReasonCodeCommand>
{
    public CreateReasonCodeCommandValidator()
    {
        RuleFor(x => x.Category).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
        RuleFor(x => x.MinNotesLength).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RiskLevel).NotEmpty().MaximumLength(20);
    }
}

public sealed class CreateReasonCodeCommandHandler : IRequestHandler<CreateReasonCodeCommand, Result<ReasonCodeDto>>
{
    private readonly IGovernanceService _governanceService;

    public CreateReasonCodeCommandHandler(IGovernanceService governanceService)
    {
        _governanceService = governanceService;
    }

    public async Task<Result<ReasonCodeDto>> Handle(CreateReasonCodeCommand request, CancellationToken cancellationToken)
    {
        var create = new CreateReasonCodeRequest(
            request.Category,
            request.Code,
            request.Label,
            request.RequiresNotes,
            request.RequiresApproval ? "APPROVAL_REQUIRED" : null);

        return await _governanceService.CreateReasonCodeAsync(create, cancellationToken);
    }
}
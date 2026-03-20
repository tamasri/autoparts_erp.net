namespace AutoPartsERP.Application.Features.Approvals.RejectRequest;

public sealed record RejectRequestCommand(Guid RequestId, string Reason)
    : IRequest<Result<ApprovalRequestDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.ApprovalsReview;
    public string AuditModule => "APPROVALS";
}

public sealed class RejectRequestCommandValidator : AbstractValidator<RejectRequestCommand>
{
    public RejectRequestCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3).MaximumLength(2000);
    }
}

public sealed class RejectRequestCommandHandler : IRequestHandler<RejectRequestCommand, Result<ApprovalRequestDto>>
{
    private readonly IGovernanceService _governanceService;
    private readonly ICurrentUser _currentUser;

    public RejectRequestCommandHandler(IGovernanceService governanceService, ICurrentUser currentUser)
    {
        _governanceService = governanceService;
        _currentUser = currentUser;
    }

    public async Task<Result<ApprovalRequestDto>> Handle(RejectRequestCommand request, CancellationToken cancellationToken)
    {
        return await _governanceService.RejectApprovalAsync(request.RequestId, request.Reason, _currentUser.UserId, cancellationToken);
    }
}
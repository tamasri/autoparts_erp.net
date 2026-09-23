namespace AutoPartsERP.Application.Features.Approvals.ApproveRequest;

public sealed record ApproveRequestCommand(Guid RequestId, string? Notes)
    : IRequest<Result<ApprovalRequestDto>>, IAuditableRequest
{
    // Authorised in IGovernanceService: approvals.review for ordinary requests, the warehouses' managers for a transfer.
    public string AuditModule => "APPROVALS";
}

public sealed class ApproveRequestCommandValidator : AbstractValidator<ApproveRequestCommand>
{
    public ApproveRequestCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class ApproveRequestCommandHandler : IRequestHandler<ApproveRequestCommand, Result<ApprovalRequestDto>>
{
    private readonly IGovernanceService _governanceService;
    private readonly ICurrentUser _currentUser;

    public ApproveRequestCommandHandler(IGovernanceService governanceService, ICurrentUser currentUser)
    {
        _governanceService = governanceService;
        _currentUser = currentUser;
    }

    public async Task<Result<ApprovalRequestDto>> Handle(ApproveRequestCommand request, CancellationToken cancellationToken)
    {
        return await _governanceService.ApproveApprovalAsync(request.RequestId, request.Notes, _currentUser.UserId, cancellationToken);
    }
}
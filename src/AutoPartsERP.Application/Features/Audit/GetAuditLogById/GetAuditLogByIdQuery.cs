namespace AutoPartsERP.Application.Features.Audit.GetAuditLogById;

public sealed record GetAuditLogByIdQuery(Guid AuditLogId)
    : IRequest<Result<AuditEntryDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.AuditRead;
}

public sealed class GetAuditLogByIdQueryValidator : AbstractValidator<GetAuditLogByIdQuery>
{
    public GetAuditLogByIdQueryValidator()
    {
        RuleFor(x => x.AuditLogId).NotEmpty();
    }
}

public sealed class GetAuditLogByIdQueryHandler : IRequestHandler<GetAuditLogByIdQuery, Result<AuditEntryDto>>
{
    private readonly IGovernanceService _governanceService;

    public GetAuditLogByIdQueryHandler(IGovernanceService governanceService)
    {
        _governanceService = governanceService;
    }

    public async Task<Result<AuditEntryDto>> Handle(GetAuditLogByIdQuery request, CancellationToken cancellationToken)
    {
        return await _governanceService.GetAuditEntryByIdAsync(request.AuditLogId, cancellationToken);
    }
}
namespace AutoPartsERP.Application.Features.Audit.GetAuditLogs;

public sealed record GetAuditLogsQuery(
    int Page,
    int PageSize,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Module,
    string? EntityType,
    Guid? EntityId,
    Guid? ActorId)
    : IRequest<Result<PagedResponse<AuditEntryDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.AuditRead;
}

public sealed class GetAuditLogsQueryValidator : AbstractValidator<GetAuditLogsQuery>
{
    public GetAuditLogsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 1000);
    }
}

public sealed class GetAuditLogsQueryHandler : IRequestHandler<GetAuditLogsQuery, Result<PagedResponse<AuditEntryDto>>>
{
    private readonly IGovernanceService _governanceService;

    public GetAuditLogsQueryHandler(IGovernanceService governanceService)
    {
        _governanceService = governanceService;
    }

    public async Task<Result<PagedResponse<AuditEntryDto>>> Handle(GetAuditLogsQuery request, CancellationToken cancellationToken)
    {
        var search = new AuditSearchRequest(
            request.Module,
            request.EntityType,
            request.EntityId?.ToString(),
            request.ActorId,
            request.From,
            request.To,
            request.Page,
            request.PageSize);

        return await _governanceService.SearchAuditAsync(search, cancellationToken);
    }
}
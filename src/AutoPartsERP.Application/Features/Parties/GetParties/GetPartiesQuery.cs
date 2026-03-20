namespace AutoPartsERP.Application.Features.Parties.GetParties;

public sealed record GetPartiesQuery(PartyQueryRequest Request)
    : IRequest<Result<PagedResponse<PartyListItemDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Party.Read;
}

public sealed class GetPartiesQueryHandler : IRequestHandler<GetPartiesQuery, Result<PagedResponse<PartyListItemDto>>>
{
    private readonly IPartyRepository _partyRepository;

    public GetPartiesQueryHandler(IPartyRepository partyRepository)
    {
        _partyRepository = partyRepository;
    }

    public async Task<Result<PagedResponse<PartyListItemDto>>> Handle(GetPartiesQuery request, CancellationToken cancellationToken)
    {
        var paged = await _partyRepository.GetPagedAsync(request.Request, cancellationToken);
        return Result<PagedResponse<PartyListItemDto>>.Success(new PagedResponse<PartyListItemDto>(
            paged.Items,
            paged.Page,
            paged.PageSize,
            paged.TotalCount));
    }
}

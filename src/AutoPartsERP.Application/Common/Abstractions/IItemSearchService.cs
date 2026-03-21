namespace AutoPartsERP.Application.Common.Abstractions;

public interface IItemSearchService
{
    Task<Result<PagedResponse<ItemSearchResultDto>>> SearchAsync(
        SearchItemsRequest request,
        CancellationToken cancellationToken = default);
}


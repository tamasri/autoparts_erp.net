namespace AutoPartsERP.Application.Common.Abstractions.Repositories;

public interface IPartyRepository
{
    Task<Party?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Party?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string code, CancellationToken cancellationToken = default);
    Task<PagedResult<PartyListItemDto>> GetPagedAsync(PartyQueryRequest query, CancellationToken cancellationToken = default);
    Task AddAsync(Party party, CancellationToken cancellationToken = default);
    Task UpdateAsync(Party party, CancellationToken cancellationToken = default);
}

namespace AutoPartsERP.Application.Common.Abstractions;

public interface IDbConnectionFactory
{
    Task<DbConnection> CreateAsync(CancellationToken cancellationToken = default);

    async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        return await CreateAsync(cancellationToken);
    }
}

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public NpgsqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration["Database:ConnectionString"]
            ?? throw new InvalidOperationException("Database connection string is missing.");
    }

    public async Task<DbConnection> CreateAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
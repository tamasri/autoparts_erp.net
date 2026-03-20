namespace AutoPartsERP.Infrastructure.Jobs;

[Queue("governance")]
public sealed class IdempotencyCleanupJob
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public IdempotencyCleanupJob(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dbConnectionFactory.CreateAsync(cancellationToken);
        await connection.ExecuteAsync("DELETE FROM idempotency_keys WHERE expires_at_utc < NOW() AND is_completed = TRUE;");
    }
}
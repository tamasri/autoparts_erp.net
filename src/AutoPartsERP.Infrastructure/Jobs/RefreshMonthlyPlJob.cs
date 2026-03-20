namespace AutoPartsERP.Infrastructure.Jobs;

public sealed class RefreshMonthlyPlJob
{
    private readonly IDbConnectionFactory _connectionFactory;

    public RefreshMonthlyPlJob(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [Queue("governance")]
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "REFRESH MATERIALIZED VIEW CONCURRENTLY monthly_pl_summary;",
            cancellationToken: cancellationToken));
    }
}

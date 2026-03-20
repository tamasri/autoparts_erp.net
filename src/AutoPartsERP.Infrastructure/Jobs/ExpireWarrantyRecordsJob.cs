namespace AutoPartsERP.Infrastructure.Jobs;

public sealed class ExpireWarrantyRecordsJob
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ExpireWarrantyRecordsJob(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [Queue("governance")]
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE warranty_records
            SET status = 'EXPIRED',
                updated_at = now()
            WHERE status = 'ACTIVE'
              AND expiry_date < CURRENT_DATE;
            """,
            cancellationToken: cancellationToken));
    }
}

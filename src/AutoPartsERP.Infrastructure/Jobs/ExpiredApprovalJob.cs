namespace AutoPartsERP.Infrastructure.Jobs;

[Queue("governance")]
public sealed class ExpiredApprovalJob
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public ExpiredApprovalJob(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dbConnectionFactory.CreateAsync(cancellationToken);
        await connection.ExecuteAsync("UPDATE approval_requests SET status = 'EXPIRED' WHERE status = 'PENDING' AND expires_at < NOW();");
    }
}
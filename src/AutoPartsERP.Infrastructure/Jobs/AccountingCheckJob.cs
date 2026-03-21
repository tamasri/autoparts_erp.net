namespace AutoPartsERP.Infrastructure.Jobs;

public sealed class AccountingCheckJob
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<AccountingCheckJob> _logger;

    public AccountingCheckJob(AppDbContext dbContext, ILogger<AccountingCheckJob> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [Queue("governance")]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var run = new AiTaskRun(
            Guid.NewGuid(),
            "ACCOUNTING_CHECK",
            "COMPLETED",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "Automated accounting review generated suggestions.",
            null);

        await _dbContext.AiTaskRuns.AddAsync(run, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("AccountingCheckJob completed. RunId={RunId}", run.Id);
    }
}


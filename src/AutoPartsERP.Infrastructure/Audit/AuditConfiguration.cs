namespace AutoPartsERP.Infrastructure.Audit;

public static class AuditConfiguration
{
    public static void Configure(IConfiguration configuration)
    {
        var connectionString = configuration["Database:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        global::Audit.Core.PostgreSqlConfiguratorExtensions.UsePostgreSql(
            global::Audit.Core.Configuration.Setup(),
            config => config
                .ConnectionString(connectionString)
                .TableName("audit_logs")
                .IdColumnName("id"));

        global::Audit.EntityFramework.Configuration.Setup()
            .ForContext<Persistence.AppDbContext>(setup => setup
                .IncludeEntityObjects()
                .AuditEventType("{context}:{action}"));
    }
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace AutoPartsERP.IntegrationTests;

public sealed class ErpWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("autoparts_erp_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder().Build();

    public bool ContainersAvailable { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = ContainersAvailable
                    ? _postgres.GetConnectionString()
                    : "Host=localhost;Port=5432;Database=autoparts_erp;Username=postgres;Password=postgres",
                ["Redis:ConnectionString"] = ContainersAvailable
                    ? _redis.GetConnectionString()
                    : "localhost:6379",
                ["Jwt:Issuer"] = "autoparts-tests",
                ["Jwt:Audience"] = "autoparts-tests",
                ["Jwt:PublicKeyPemBase64"] = string.Empty,
                ["Jwt:PrivateKeyPemBase64"] = string.Empty
            };

            configurationBuilder.AddInMemoryCollection(settings);
        });
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _postgres.StartAsync();
            await _redis.StartAsync();
            ContainersAvailable = true;
        }
        catch
        {
            ContainersAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }
}

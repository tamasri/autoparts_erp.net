using System.Net;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.IntegrationTests;

public sealed class AuditIntegrationTests : IClassFixture<ErpWebFactory>
{
    private readonly HttpClient _client;

    public AuditIntegrationTests(ErpWebFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAuditLogs_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var response = await _client.GetAsync("/api/v1/audit?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAuditLogById_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var response = await _client.GetAsync($"/api/v1/audit/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAuditLogsByModule_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var response = await _client.GetAsync("/api/v1/audit?page=1&pageSize=10&module=USERS");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAuditLogsByEntityType_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var response = await _client.GetAsync("/api/v1/audit?page=1&pageSize=10&entityType=AppUser");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

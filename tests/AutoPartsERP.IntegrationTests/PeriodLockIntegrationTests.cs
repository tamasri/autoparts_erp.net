using System.Net;
using System.Net.Http.Json;
using AutoPartsERP.Contracts.Periods;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.IntegrationTests;

public sealed class PeriodLockIntegrationTests : IClassFixture<ErpWebFactory>
{
    private readonly HttpClient _client;

    public PeriodLockIntegrationTests(ErpWebFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetPeriodLocks_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var response = await _client.GetAsync("/api/v1/periods/locks?year=2026&month=1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LockPeriod_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/periods/lock",
            new LockPeriodRequest("2026-01", "USERS", "Close period for governance test."));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

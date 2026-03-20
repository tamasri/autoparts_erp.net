using System.Net;
using System.Net.Http.Json;
using AutoPartsERP.Contracts.Parties;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.IntegrationTests;

public sealed class PartyIntegrationTests : IClassFixture<ErpWebFactory>
{
    private readonly HttpClient _client;

    public PartyIntegrationTests(ErpWebFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetParties_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var response = await _client.GetAsync("/api/v1/parties?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateParty_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var payload = new CreatePartyRequest(
            "Integration Party",
            "طرف تكاملي",
            null,
            null,
            null,
            new[] { "CUSTOMER" });

        var response = await _client.PostAsJsonAsync("/api/v1/parties", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequestPartyType_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var payload = new RequestPartyTypeAssignmentRequest("VENDOR", "Need AP flow");
        var response = await _client.PostAsJsonAsync($"/api/v1/parties/{Guid.NewGuid()}/types", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPartyCombinedStatement_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var response = await _client.GetAsync($"/api/v1/parties/{Guid.NewGuid()}/statement/combined");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

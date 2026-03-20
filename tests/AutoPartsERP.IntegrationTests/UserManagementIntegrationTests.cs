using System.Net;
using System.Net.Http.Json;
using AutoPartsERP.Contracts.Users;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.IntegrationTests;

public sealed class UserManagementIntegrationTests : IClassFixture<ErpWebFactory>
{
    private readonly HttpClient _client;

    public UserManagementIntegrationTests(ErpWebFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetUsers_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var response = await _client.GetAsync("/api/v1/users?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUserById_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var response = await _client.GetAsync($"/api/v1/users/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateUser_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var request = new CreateUserRequest(
            "integration.user",
            "integration.user@example.com",
            "Integration",
            "User",
            "Password123!",
            new[] { Guid.NewGuid() });

        var response = await _client.PostAsJsonAsync("/api/v1/users", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateUser_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/users/{Guid.NewGuid()}",
            new UpdateUserRequest("user@example.com", "First", "Last", true, new[] { Guid.NewGuid() }));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeactivateUser_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var payload = new { Reason = "Security policy update.", ReasonCode = "SEC_POLICY" };
        var response = await _client.PostAsJsonAsync($"/api/v1/users/{Guid.NewGuid()}/deactivate", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

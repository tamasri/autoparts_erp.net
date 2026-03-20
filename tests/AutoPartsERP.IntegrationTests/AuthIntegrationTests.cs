using System.Net;
using System.Net.Http.Json;
using AutoPartsERP.Contracts.Auth;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.IntegrationTests;

public sealed class AuthIntegrationTests : IClassFixture<ErpWebFactory>
{
    private readonly HttpClient _client;

    public AuthIntegrationTests(ErpWebFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_ShouldReturnBadRequest_WhenPayloadIsEmpty()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(string.Empty, string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_ShouldReturnBadRequest_WhenPasswordMissing()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("user@example.com", string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_ShouldReturnBadRequest_WhenTokenMissing()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_ShouldReturnBadRequest_WhenTokenWhitespace()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(" "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Logout_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/logout", new LogoutRequest("token"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var response = await _client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

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

    private static HttpRequestMessage Post(string path, bool csrfHeader)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (csrfHeader)
        {
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        }

        return request;
    }

    [Fact]
    public async Task Refresh_ShouldBeForbidden_WithoutTheCsrfHeader()
    {
        var response = await _client.SendAsync(Post("/api/v1/auth/refresh", csrfHeader: false));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Refresh_ShouldReturnUnauthorized_WithoutTheSessionCookie()
    {
        var response = await _client.SendAsync(Post("/api/v1/auth/refresh", csrfHeader: true));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_IgnoresATokenInTheBody()
    {
        var request = Post("/api/v1/auth/refresh", csrfHeader: true);
        request.Content = JsonContent.Create(new { refreshToken = "anything" });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ShouldBeForbidden_WithoutTheCsrfHeader()
    {
        var response = await _client.SendAsync(Post("/api/v1/auth/logout", csrfHeader: false));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Logout_ClearsTheSessionCookie()
    {
        var response = await _client.SendAsync(Post("/api/v1/auth/logout", csrfHeader: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith("erp_rt=;") && c.Contains("httponly") && c.Contains("path=/api/v1/auth"));
    }

    [Fact]
    public async Task Me_ShouldReturnUnauthorized_WhenNoBearerToken()
    {
        var response = await _client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

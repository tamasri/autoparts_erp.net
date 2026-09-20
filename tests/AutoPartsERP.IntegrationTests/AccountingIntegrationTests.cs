using System.Net;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.IntegrationTests;

public sealed class AccountingIntegrationTests : IClassFixture<ErpWebFactory>
{
    private readonly HttpClient _client;

    public AccountingIntegrationTests(ErpWebFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/api/v1/accounting/accounts")]
    [InlineData("/api/v1/accounting/entries")]
    [InlineData("/api/v1/accounting/entry-types")]
    [InlineData("/api/v1/accounting/tags")]
    [InlineData("/api/v1/accounting/reports/trial-balance?from=2026-01-01&to=2026-12-31")]
    [InlineData("/api/v1/accounting/reports/balance-sheet?asOf=2026-12-31")]
    [InlineData("/api/v1/accounting/reports/party-balances?partyType=CUSTOMER&asOf=2026-12-31")]
    [InlineData("/api/v1/accounting/reconciliation")]
    public async Task AccountingEndpoints_ShouldReturnUnauthorized_WhenNoBearerToken(string url)
    {
        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

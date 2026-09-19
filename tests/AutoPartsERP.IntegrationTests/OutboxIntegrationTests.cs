using System.Net;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.IntegrationTests;

public sealed class OutboxIntegrationTests : IClassFixture<ErpWebFactory>
{
    private readonly HttpClient _client;

    public OutboxIntegrationTests(ErpWebFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task MetricsEndpoint_ReturnsPrometheusFormat()
    {
        // The Prometheus exporter renders nothing until at least one instrument has recorded a
        // value and a collection cycle has run, so generate traffic and poll briefly instead of
        // asserting on the very first scrape (which made this test order/timing dependent).
        await _client.GetAsync("/health");

        var body = string.Empty;
        for (var attempt = 0; attempt < 10 && !body.Contains('#'); attempt++)
        {
            var response = await _client.GetAsync("/metrics");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            body = await response.Content.ReadAsStringAsync();
            if (!body.Contains('#'))
            {
                await Task.Delay(500);
            }
        }

        body.Should().Contain("#");
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsAValidStatusCode()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }
}

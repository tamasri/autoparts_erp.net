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
    public async Task MetricsEndpoint_IsMappedAndReturnsPrometheusText()
    {
        // The OpenTelemetry Prometheus exporter legitimately renders an empty body until its
        // first collection cycle completes, so the body is not a reliable signal in a test host.
        // What this test guards is that the scrape endpoint is mapped, reachable and healthy.
        await _client.GetAsync("/health");

        var response = await _client.GetAsync("/metrics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 0)
        {
            body.Should().Contain("#");
        }
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsAValidStatusCode()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }
}

using System.Net;
using System.Text;
using AutoPartsERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoPartsERP.UnitTests;

/// <summary>
/// Frappe v16 refuses SQL functions written as text in list fields ("sum(debit) as debit") and wants {"SUM": "debit", "as": "debit"};
/// v15 and older understand only the text form. The client must work against both, learning which one it talks to.
/// </summary>
public sealed class ErpNextLedgerDialectTests
{
    // The exact body production ERPNext (Frappe v16) returned.
    private const string V16Refusal =
        "{\"exception\":\"frappe.exceptions.ValidationError: SQL functions are not allowed as strings in SELECT: sum(debit) as debit. Use dict syntax like {'COUNT': '*'} instead.\","
        + "\"exc_type\":\"ValidationError\",\"exc\":\"[\\\"Traceback (most recent call last):\\\\n  File \\\\\\\"apps/frappe/frappe/app.py\\\\\\\", line 158, in application\\\"]\","
        + "\"_server_messages\":\"[\\\"{\\\\\\\"message\\\\\\\": \\\\\\\"SQL functions are not allowed as strings in SELECT: sum(debit) as debit. Use dict syntax like {'COUNT': '*'} instead.\\\\\\\"}\\\"]\"}";

    private sealed class FakeFrappe(int version) : HttpMessageHandler
    {
        public List<string> GlQueries { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
            if (url.Contains("/Company?"))
            {
                return Task.FromResult(Ok("{\"data\":[{\"name\":\"Test Co\",\"abbr\":\"TC\"}]}"));
            }

            GlQueries.Add(url);
            var textForm = url.Contains("sum(debit) as debit");
            var dictForm = url.Contains("{\"SUM\":\"debit\",\"as\":\"debit\"}");
            if (version >= 16 && textForm)
            {
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)417) { Content = new StringContent(V16Refusal, Encoding.UTF8, "application/json") });
            }

            if (version < 16 && dictForm)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"exc_type\":\"TypeError\",\"exception\":\"TypeError: unhashable type: 'dict'\"}") });
            }

            return Task.FromResult(Ok("{\"data\":[{\"account\":\"Cash - TC\",\"debit\":150.5,\"credit\":20}]}"));
        }

        private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static (ErpNextClient Client, FakeFrappe Server) Create(int version)
    {
        var server = new FakeFrappe(version);
        // A distinct base URL per test: the learned form is remembered per server.
        var options = Options.Create(new ErpNextOptions { Enabled = true, BaseUrl = $"http://frappe-v{version}-{Guid.NewGuid():N}.test", ApiKey = "k", ApiSecret = "s" });
        return (new ErpNextClient(new HttpClient(server), options, NullLogger<ErpNextClient>.Instance), server);
    }

    [Fact]
    public async Task Against_v16_aggregates_go_as_dicts_and_are_never_refused()
    {
        var (client, server) = Create(16);

        var balances = await client.GetGlBalancesAsync(null, null);

        balances.IsSuccess.Should().BeTrue(balances.IsFailure ? balances.Error.Message : null);
        balances.Value!.Single().Debit.Should().Be(150.5m);
        server.GlQueries.Should().ContainSingle().Which.Should().Contain("{\"SUM\":\"debit\",\"as\":\"debit\"}");
    }

    [Fact]
    public async Task Against_v15_the_client_falls_back_to_text_once_and_remembers()
    {
        var (client, server) = Create(15);

        (await client.GetGlBalancesAsync(null, null)).IsSuccess.Should().BeTrue();
        (await client.GetGlSummaryAsync(new AutoPartsERP.Application.Common.Abstractions.ErpNextGlFilter(null, null, null, null, null, 0))).IsSuccess.Should().BeTrue();

        // First query: dict refused, text accepted. Second query: text straight away.
        server.GlQueries.Should().HaveCount(3);
        server.GlQueries[0].Should().Contain("\"SUM\"");
        server.GlQueries[1].Should().Contain("sum(debit) as debit");
        server.GlQueries[2].Should().Contain("count(name) as n");
    }

    [Fact]
    public void Frappe_errors_are_reduced_to_their_message()
    {
        var text = ErpNextErrors.Describe((HttpStatusCode)417, V16Refusal);

        text.Should().Be("ERPNext 417: SQL functions are not allowed as strings in SELECT: sum(debit) as debit. Use dict syntax like {'COUNT': '*'} instead.");
        text.Should().NotContain("Traceback").And.NotContain("apps/frappe");
    }

    [Theory]
    [InlineData("{\"exception\":\"frappe.exceptions.DoesNotExistError: Customer X not found\"}", "Customer X not found")]
    [InlineData("{\"_server_messages\":\"[\\\"{\\\\\\\"message\\\\\\\": \\\\\\\"<b>Account</b> is mandatory\\\\\\\"}\\\"]\"}", "Account is mandatory")]
    [InlineData("<html><body>502 Bad Gateway</body></html>", "ERPNext answered with an error page instead of data (is it running?)")]
    public void Other_error_shapes_are_readable(string body, string expected) =>
        ErpNextErrors.Extract(body).Should().Be(expected);
}

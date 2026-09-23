using System.Net;
using System.Text;
using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Domain.Constants;
using AutoPartsERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoPartsERP.UnitTests;

/// <summary>
/// ERPNext names a second party with the same name "X - 1" instead of refusing it, so a party is looked up before it is created.
/// A failed lookup must not be read as "not there": that would create the duplicate the lookup exists to prevent.
/// </summary>
public sealed class ErpNextPartyUpsertTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (ErpNextClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var options = Options.Create(new ErpNextOptions { Enabled = true, BaseUrl = "http://erpnext.test", ApiKey = "k", ApiSecret = "s" });
        return (new ErpNextClient(new HttpClient(handler), options, NullLogger<ErpNextClient>.Instance), handler);
    }

    private static readonly ErpNextPartySync Customer = new(Guid.NewGuid(), "Garage One", PartyTypeCodes.Customer, null);

    [Fact]
    public async Task Failed_lookup_returns_failure_and_creates_nothing()
    {
        var (client, handler) = Create(_ => Json(HttpStatusCode.BadGateway, "{}"));

        var result = await client.SyncPartyAsync(Customer);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ErpNext.LookupFailed");
        handler.Methods.Should().Equal(HttpMethod.Get);
    }

    [Fact]
    public async Task Unreadable_lookup_returns_failure_and_creates_nothing()
    {
        var (client, handler) = Create(_ => Json(HttpStatusCode.OK, "<html>maintenance</html>"));

        var result = await client.SyncPartyAsync(Customer);

        result.IsFailure.Should().BeTrue();
        handler.Methods.Should().Equal(HttpMethod.Get);
    }

    [Fact]
    public async Task Party_not_found_is_created()
    {
        var (client, handler) = Create(r => r.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, "{\"data\":[]}")
            : Json(HttpStatusCode.OK, "{\"data\":{\"name\":\"Garage One\"}}"));

        var result = await client.SyncPartyAsync(Customer);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Garage One");
        handler.Methods.Should().Equal(HttpMethod.Get, HttpMethod.Post);
    }

    [Fact]
    public async Task Existing_party_is_updated_in_place()
    {
        var (client, handler) = Create(r => r.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, "{\"data\":[{\"name\":\"CUST-0007\"}]}")
            : Json(HttpStatusCode.OK, "{\"data\":{\"name\":\"CUST-0007\"}}"));

        var result = await client.SyncPartyAsync(Customer);

        result.Value.Should().Be("CUST-0007");
        handler.Methods.Should().Equal(HttpMethod.Get, HttpMethod.Put);
    }
}

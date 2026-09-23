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

    [Fact]
    public async Task A_linked_party_is_updated_by_its_record_name_without_any_lookup()
    {
        // The display name changed ("Garage One" → "Garage One Ltd"); the record keeps its name, so its invoices stay attached.
        string? putUrl = null;
        var (client, handler) = Create(r => { putUrl = r.RequestUri!.AbsolutePath; return Json(HttpStatusCode.OK, "{\"data\":{\"name\":\"CUST-00042\"}}"); });

        var result = await client.SyncPartyAsync(Customer with { Name = "Garage One Ltd", KnownName = "CUST-00042" });

        result.Value.Should().Be("CUST-00042");
        handler.Methods.Should().Equal(HttpMethod.Put);
        Uri.UnescapeDataString(putUrl!).Should().EndWith("/Customer/CUST-00042");
    }

    [Fact]
    public async Task A_linked_record_deleted_in_ErpNext_is_linked_again()
    {
        var (client, handler) = Create(r => r.Method == HttpMethod.Put ? Json(HttpStatusCode.NotFound, "{\"exc_type\":\"DoesNotExistError\"}")
            : r.Method == HttpMethod.Get ? Json(HttpStatusCode.OK, "{\"data\":[]}")
            : Json(HttpStatusCode.OK, "{\"data\":{\"name\":\"Garage One\"}}"));

        var result = await client.SyncPartyAsync(Customer with { KnownName = "CUST-00042" });

        result.Value.Should().Be("Garage One");
        handler.Methods.Should().Equal(HttpMethod.Put, HttpMethod.Get, HttpMethod.Post);
    }

    [Fact]
    public async Task A_same_named_record_owned_by_another_party_is_not_adopted()
    {
        // Two different customers are both called "Garage One": the second gets its own record, told apart by its code.
        string? postedName = null;
        var (client, handler) = Create(r =>
        {
            if (r.Method == HttpMethod.Get) return Json(HttpStatusCode.OK, "{\"data\":[{\"name\":\"Garage One\"}]}");
            postedName = System.Text.Json.JsonDocument.Parse(r.Content!.ReadAsStringAsync().Result).RootElement.GetProperty("customer_name").GetString();
            return Json(HttpStatusCode.OK, "{\"data\":{\"name\":\"Garage One (WS-0007)\"}}");
        });

        var result = await client.SyncPartyAsync(Customer with { Code = "WS-0007", TakenNames = ["Garage One"] });

        result.Value.Should().Be("Garage One (WS-0007)");
        postedName.Should().Be("Garage One (WS-0007)");
        handler.Methods.Should().Equal(HttpMethod.Get, HttpMethod.Post);
    }

    [Fact]
    public async Task Of_several_same_named_records_the_one_nobody_owns_is_adopted()
    {
        var (client, handler) = Create(r => r.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, "{\"data\":[{\"name\":\"Garage One\"},{\"name\":\"Garage One - 1\"}]}")
            : Json(HttpStatusCode.OK, "{\"data\":{\"name\":\"Garage One - 1\"}}"));

        var result = await client.SyncPartyAsync(Customer with { TakenNames = ["Garage One"] });

        result.Value.Should().Be("Garage One - 1");
        handler.Methods.Should().Equal(HttpMethod.Get, HttpMethod.Put);
    }
}

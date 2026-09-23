using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AutoPartsERP.Application.Features.Assistant;
using AutoPartsERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class AssistantTests
{
    [Theory]
    [InlineData("كم رصيد محمد الخطيب؟", AssistantActions.CustomerBalance, "customer_name", "محمد الخطيب")]
    [InlineData("رصيد الزبون شركه الافق", AssistantActions.CustomerBalance, "customer_name", "شركه الافق")]
    [InlineData("كم يوجد فلتر زيت تويوتا", AssistantActions.ItemStock, "item", "فلتر زيت تويوتا")]
    [InlineData("مخزون 04152-YZZA1", AssistantActions.ItemStock, "item", "04152-YZZA1")]
    [InlineData("فاتورة ٤١", AssistantActions.InvoiceStatus, "invoice_number", "٤١")]
    [InlineData("وين صارت الفاتورة رقم INV-2026-00041", AssistantActions.InvoiceStatus, "invoice_number", "INV-2026-00041")]
    [InlineData("مبيعات اليوم", AssistantActions.SalesSummary, "period", "today")]
    [InlineData("كم المبيعات هذا الشهر", AssistantActions.SalesSummary, "period", "this_month")]
    [InlineData("مبيعات امبارح امس", AssistantActions.SalesSummary, "period", "yesterday")]
    [InlineData("الفواتير المتأخرة لشركة الأفق", AssistantActions.OverdueInvoices, "customer_name", "شركة الأفق")]
    public void Keyword_rules_understand_the_common_questions(string text, string action, string arg, string value)
    {
        var intent = RuleBasedIntents.Parse(text);

        intent.Action.Should().Be(action);
        intent.Arg(arg).Should().Be(value);
    }

    [Theory]
    [InlineData("مرحبا")]
    [InlineData("شو بتعرف تعمل؟")]
    [InlineData("احذف كل الفواتير")]
    public void Anything_else_is_help(string text) => RuleBasedIntents.Parse(text).Action.Should().Be(AssistantActions.Help);

    private static MatchCandidate C(string label, double score) => new(Guid.NewGuid(), label, null, score);

    [Fact]
    public void A_clear_winner_is_taken() =>
        EntityMatcher.Decide([C("محمد الخطيب", 0.92), C("محمود الخطيب", 0.55)]).Kind.Should().Be(MatchKind.Found);

    [Fact]
    public void Close_names_are_asked_not_guessed()
    {
        // "حسن" vs "حسين": both plausible, too close to choose.
        var outcome = EntityMatcher.Decide([C("حسن", 0.8), C("حسين", 0.72)]);

        outcome.Kind.Should().Be(MatchKind.Ambiguous);
        outcome.Candidates.Select(c => c.Label).Should().Equal("حسن", "حسين");
    }

    [Fact]
    public void A_typo_with_one_candidate_is_confirmed_not_assumed()
    {
        // "محمذ" → "محمد" scores ~0.43: offered as a choice, never used silently.
        var outcome = EntityMatcher.Decide([C("محمد", 0.43)]);

        outcome.Kind.Should().Be(MatchKind.Ambiguous);
        outcome.Candidates.Should().ContainSingle();
    }

    [Fact]
    public void Weak_matches_are_not_offered()
    {
        EntityMatcher.Decide([C("شيء بعيد", 0.2)]).Kind.Should().Be(MatchKind.NotFound);
        EntityMatcher.Decide([]).Kind.Should().Be(MatchKind.NotFound);
    }

    [Fact]
    public void At_most_five_choices_best_first()
    {
        var outcome = EntityMatcher.Decide(Enumerable.Range(1, 9).Select(i => C($"n{i}", 0.5 + (i / 100.0))).ToList());

        outcome.Candidates.Should().HaveCount(EntityMatcher.MaxChoices);
        outcome.Candidates[0].Label.Should().Be("n9");
    }

    [Theory]
    [InlineData("963933123456@s.whatsapp.net", "963933123456")]
    [InlineData("963933123456:17@s.whatsapp.net", "963933123456")]
    [InlineData("123456789012345@lid", null)]
    [InlineData("12036302@g.us", null)]
    [InlineData(null, null)]
    public void Phone_numbers_come_only_from_phone_ids(string? jid, string? expected) => WhatsAppAssistant.PhoneDigits(jid).Should().Be(expected);

    [Theory]
    [InlineData("+963 933-123-456", "963933123456")]
    [InlineData("00963933123456", "963933123456")]
    [InlineData("٩٦٣٩٣٣١٢٣٤٥٦", "963933123456")]
    public void Admin_phone_input_is_normalized(string raw, string expected) => AssistantLinks.NormalizePhone(raw).Value.Should().Be(expected);

    [Theory]
    [InlineData("0933123456")]
    [InlineData("12345")]
    public void Local_or_short_numbers_are_refused(string raw) => AssistantLinks.NormalizePhone(raw).IsFailure.Should().BeTrue();

    [Fact]
    public void Link_codes_are_six_digits_and_stored_hashed()
    {
        var code = AssistantLinks.NewCode();

        code.Should().MatchRegex("^[0-9]{6}$");
        AssistantLinks.HashCode(code).Should().NotContain(code).And.HaveLength(64);
    }

    private sealed class Stub(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request, LastBody ?? string.Empty);
        }
    }

    private static (GroqIntentExtractor Extractor, Stub Stub) Groq(string responseJson, HttpStatusCode status = HttpStatusCode.OK, string key = "test-key")
    {
        var stub = new Stub((_, _) => new HttpResponseMessage(status) { Content = new StringContent(responseJson, Encoding.UTF8, "application/json") });
        var options = Options.Create(new AiOptions { ApiKey = key, BaseUrl = "http://groq.test/openai/v1", Model = "m" });
        return (new GroqIntentExtractor(new HttpClient(stub), options, NullLogger<GroqIntentExtractor>.Instance), stub);
    }

    private static string ToolCall(string name, string argumentsJson) =>
        new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject
            {
                ["message"] = new JsonObject
                {
                    ["tool_calls"] = new JsonArray(new JsonObject { ["function"] = new JsonObject { ["name"] = name, ["arguments"] = argumentsJson } }),
                },
            }),
        }.ToJsonString();

    [Fact]
    public async Task The_model_call_becomes_an_intent_and_carries_only_the_message()
    {
        var (groq, stub) = Groq(ToolCall("customer_balance", "{\"customer_name\":\"محمذ\"}"));

        var intent = await groq.ExtractAsync("كم رصيد محمذ");

        intent.Value!.Action.Should().Be(AssistantActions.CustomerBalance);
        intent.Value.Arg("customer_name").Should().Be("محمذ");
        var sent = JsonNode.Parse(stub.LastBody!)!;
        sent["messages"]!.AsArray().Should().HaveCount(2);
        sent["messages"]![1]!["content"]!.GetValue<string>().Should().Be("كم رصيد محمذ");
        sent["tool_choice"]!.GetValue<string>().Should().Be("required");
        sent["tools"]!.AsArray().Should().HaveCount(AssistantActions.Tools.Count);
    }

    [Fact]
    public async Task Undeclared_or_oversized_arguments_from_the_model_are_dropped()
    {
        var (groq, _) = Groq(ToolCall("item_stock", "{\"item\":\"فلتر\",\"sql\":\"drop table x\",\"warehouse\":\"" + new string('x', 500) + "\"}"));

        var intent = (await groq.ExtractAsync("x")).Value!;

        intent.Args.Keys.Should().Equal("item");
    }

    [Fact]
    public async Task Unknown_tools_errors_and_missing_key_are_failures()
    {
        (await Groq(ToolCall("delete_everything", "{}")).Extractor.ExtractAsync("x")).IsFailure.Should().BeTrue();
        (await Groq("{\"choices\":[{\"message\":{\"content\":\"hello\"}}]}").Extractor.ExtractAsync("x")).IsFailure.Should().BeTrue();
        (await Groq("{}", HttpStatusCode.TooManyRequests).Extractor.ExtractAsync("x")).IsFailure.Should().BeTrue();
        var (noKey, _) = Groq("{}", key: string.Empty);
        noKey.IsConfigured.Should().BeFalse();
        (await noKey.ExtractAsync("x")).IsFailure.Should().BeTrue();
    }
}

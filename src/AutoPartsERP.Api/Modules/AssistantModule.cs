using AutoPartsERP.Application.Features.Assistant;
using AutoPartsERP.Contracts.Assistant;

namespace AutoPartsERP.Api.Modules;

/// <summary>
/// WhatsApp assistant.
/// <para><b>/internal/assistant/*</b> — called only by the WhatsApp gateway container over the private Docker network. nginx does not
/// route <c>/internal</c>, the API port is not published, and every call must carry the shared secret (<c>Assistant:GatewaySecret</c>);
/// without a configured secret these endpoints do not exist (404).</para>
/// <para><b>/api/v1/assistant/*</b> — the administrator's screen: link/revoke numbers, gateway status and pairing QR.</para>
/// </summary>
public sealed class AssistantModule : ICarterModule
{
    public const string SecretHeader = "X-Gateway-Secret";

    public sealed record InboundPayload(string MessageId, string From, string? FromAlt, string Text);

    public sealed record GatewayStatusPayload(string State, string? Qr, string? Account);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var gateway = app.MapGroup("/internal/assistant").AllowAnonymous().ExcludeFromDescription();

        gateway.MapPost("/whatsapp/inbound", async Task<IResult> (InboundPayload payload, HttpContext http, WhatsAppAssistant assistant, CancellationToken ct) =>
        {
            if (!GatewayAuthorized(http))
            {
                return Results.NotFound();
            }

            var reply = await assistant.HandleAsync(new InboundMessage(payload.MessageId, payload.From, payload.FromAlt, payload.Text), ct);
            return Results.Ok(new { reply });
        });

        gateway.MapPost("/gateway/status", async Task<IResult> (GatewayStatusPayload payload, HttpContext http, IAssistantState state, CancellationToken ct) =>
        {
            if (!GatewayAuthorized(http))
            {
                return Results.NotFound();
            }

            var qr = payload.State == "qr" && payload.Qr is { Length: < 2000 } ? payload.Qr : null;
            await state.SetGatewayStatusAsync(new GatewayStatus(payload.State, qr, payload.Account, DateTimeOffset.UtcNow), ct);
            return Results.Ok();
        });

        var admin = app.MapGroup("/api/v1/assistant").RequireAuthorization();

        admin.MapGet("/status", async (ISender sender, CancellationToken ct) => (await sender.Send(new GetAssistantStatusQuery(), ct)).ToApiResult());

        admin.MapGet("/links", async (ISender sender, CancellationToken ct) => (await sender.Send(new ListAssistantLinksQuery(), ct)).ToApiResult());

        admin.MapPost("/links", async (CreateAssistantLinkRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new CreateAssistantLinkCommand(request.UserId, request.Phone), ct)).ToApiResult());

        admin.MapPost("/links/{id:guid}/code", async (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new RenewAssistantLinkCodeCommand(id), ct)).ToApiResult());

        admin.MapPost("/links/{id:guid}/revoke", async (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new RevokeAssistantLinkCommand(id), ct)).ToApiResult());
    }

    private static bool GatewayAuthorized(HttpContext http)
    {
        var expected = http.RequestServices.GetRequiredService<IConfiguration>()["Assistant:GatewaySecret"];
        if (string.IsNullOrWhiteSpace(expected) || expected.Length < 24)
        {
            return false;
        }

        var given = http.Request.Headers[SecretHeader].ToString();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected));
    }
}

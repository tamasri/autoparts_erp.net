namespace AutoPartsERP.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var providedValue) &&
                            Guid.TryParse(providedValue, out var parsed)
            ? parsed
            : Guid.NewGuid();

        context.TraceIdentifier = correlationId.ToString();
        context.Response.Headers[HeaderName] = correlationId.ToString();

        await _next(context);
    }
}

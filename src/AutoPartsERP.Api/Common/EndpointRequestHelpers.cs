namespace AutoPartsERP.Api.Common;

public static class EndpointRequestHelpers
{
    public static string GetIdempotencyKey(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var key) &&
            !string.IsNullOrWhiteSpace(key))
        {
            return key.ToString();
        }

        return httpContext.TraceIdentifier;
    }

    public static bool TryParsePeriodKey(string periodKey, out int year, out int month)
    {
        year = 0;
        month = 0;

        if (string.IsNullOrWhiteSpace(periodKey))
        {
            return false;
        }

        var parts = periodKey.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        return int.TryParse(parts[0], out year) &&
               int.TryParse(parts[1], out month) &&
               year is >= 2020 and <= 2100 &&
               month is >= 1 and <= 12;
    }
}

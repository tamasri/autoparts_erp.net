using System.Text.RegularExpressions;

namespace AutoPartsERP.Infrastructure.Services;

/// <summary>
/// Turns a Frappe/ERPNext error response into the sentence a person needs. Frappe answers errors with JSON that carries the
/// user-facing text in <c>_server_messages</c> (a JSON array of JSON objects), the exception line in <c>exception</c>, and a full
/// Python traceback in <c>exc</c>. Only the message is returned (it is stored in the sync log and shown on screens); the whole body
/// is written to the server log by the caller, so nothing is lost for troubleshooting.
/// </summary>
public static partial class ErpNextErrors
{
    private const int MaxLength = 400;

    public static string Describe(System.Net.HttpStatusCode status, string? body)
    {
        var message = Extract(body);
        var code = (int)status;
        return string.IsNullOrWhiteSpace(message) ? $"ERPNext HTTP {code}" : $"ERPNext {code}: {message}";
    }

    public static string? Extract(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var text = body.TrimStart();
        if (!text.StartsWith('{'))
        {
            // An HTML error page (proxy, maintenance, 502) says nothing useful to a person.
            return text.StartsWith('<') ? "ERPNext answered with an error page instead of data (is it running?)" : Clip(text);
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.TryGetProperty("_server_messages", out var sm) && sm.ValueKind == JsonValueKind.String && ServerMessages(sm.GetString()) is { Length: > 0 } messages)
            {
                return Clip(messages);
            }

            if (root.TryGetProperty("exception", out var ex) && ex.ValueKind == JsonValueKind.String && ex.GetString() is { Length: > 0 } exception)
            {
                // "frappe.exceptions.ValidationError: <text>" → "<text>"
                var colon = exception.IndexOf(": ", StringComparison.Ordinal);
                return Clip(colon > 0 && exception[..colon].Contains('.') ? exception[(colon + 2)..] : exception);
            }

            if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String && m.GetString() is { Length: > 0 } msg)
            {
                return Clip(msg);
            }

            if (root.TryGetProperty("exc_type", out var t) && t.ValueKind == JsonValueKind.String)
            {
                return t.GetString();
            }
        }
        catch (JsonException)
        {
            // fall through: not the JSON it claimed to be
        }

        return Clip(text);
    }

    private static string? ServerMessages(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var list = JsonDocument.Parse(raw);
            var parts = new List<string>();
            foreach (var item in list.RootElement.EnumerateArray())
            {
                var entry = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }

                try
                {
                    using var inner = JsonDocument.Parse(entry);
                    if (inner.RootElement.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(msg.GetString()!);
                        continue;
                    }
                }
                catch (JsonException)
                {
                    // a plain string message
                }

                parts.Add(entry);
            }

            return string.Join(" | ", parts.Select(StripHtml).Where(p => p.Length > 0).Distinct());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string StripHtml(string s) => Whitespace().Replace(System.Net.WebUtility.HtmlDecode(Tags().Replace(s, " ")), " ").Trim();

    private static string Clip(string s)
    {
        var clean = StripHtml(s);
        return clean.Length > MaxLength ? clean[..MaxLength] + "…" : clean;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

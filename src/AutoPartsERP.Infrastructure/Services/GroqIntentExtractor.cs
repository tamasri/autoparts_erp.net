using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AutoPartsERP.Application.Features.Assistant;

namespace AutoPartsERP.Infrastructure.Services;

/// <summary>Language-model settings (section <c>Ai</c>); any OpenAI-compatible endpoint. The key lives only in the server's environment.</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";

    public string Model { get; set; } = "llama-3.3-70b-versatile";

    public string ApiKey { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>
/// Intent extraction with OpenAI-style function calling (Groq by default). The request carries the system instructions, the tool
/// list and the user's message — nothing from the database. The model must call exactly one tool; its name and arguments are the
/// whole result. Any failure (no key, timeout, error, no tool call) is returned as a failure and the assistant falls back to keyword rules.
/// </summary>
public sealed class GroqIntentExtractor : IIntentExtractor
{
    private const string SystemPrompt =
        """
        You route WhatsApp messages from the owner of an auto-parts business in Syria to exactly one tool.
        Messages are usually in Arabic (often Syrian dialect, often with spelling mistakes), sometimes in English.
        Always call exactly one tool. Copy customer names, item names and numbers exactly as the user wrote them:
        do not translate, correct, complete or guess them. You have no access to any data and must never answer the question yourself.
        If the message is a greeting, asks what you can do, or fits no other tool, call "help".
        """;

    private readonly HttpClient _http;
    private readonly AiOptions _options;
    private readonly ILogger<GroqIntentExtractor> _logger;

    public GroqIntentExtractor(HttpClient http, IOptions<AiOptions> options, ILogger<GroqIntentExtractor> logger)
    {
        _options = options.Value;
        _logger = logger;
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 3, 60));
        if (IsConfigured)
        {
            _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
            _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey) && !string.IsNullOrWhiteSpace(_options.BaseUrl);

    public string ModelName => _options.Model;

    public async Task<Result<AssistantIntent>> ExtractAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Result<AssistantIntent>.Failure(new Error("Ai.ProviderNotConfigured", "No language-model key is configured."));
        }

        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["temperature"] = 0,
            ["tool_choice"] = "required",
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = text }),
            ["tools"] = new JsonArray(AssistantActions.Tools.Select(ToolJson).ToArray<JsonNode?>()),
        };

        try
        {
            using var response = await _http.PostAsJsonAsync("chat/completions", body, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result<AssistantIntent>.Failure(new Error("Ai.ProviderError", $"{(int)response.StatusCode}: {(raw.Length > 300 ? raw[..300] : raw)}"));
            }

            var call = JsonNode.Parse(raw)?["choices"]?[0]?["message"]?["tool_calls"]?[0]?["function"];
            var name = call?["name"]?.GetValue<string>();
            if (name is null || AssistantActions.Tools.All(t => t.Name != name))
            {
                return Result<AssistantIntent>.Failure(new Error("Ai.NoToolCall", "The model did not choose a known tool."));
            }

            var args = new Dictionary<string, string>(StringComparer.Ordinal);
            if (call?["arguments"]?.GetValue<string>() is { Length: > 0 } json && JsonNode.Parse(json) is JsonObject obj)
            {
                var allowed = AssistantActions.Tools.First(t => t.Name == name).Parameters;
                foreach (var (key, value) in obj)
                {
                    // Only declared parameters, only strings/numbers, bounded length: the model's output is data, not instructions.
                    if (allowed.Any(p => p.Name == key) && value is JsonValue v && v.ToString() is { Length: > 0 and <= 200 } s)
                    {
                        args[key] = s;
                    }
                }
            }

            return Result<AssistantIntent>.Success(new AssistantIntent(name, args));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or FormatException)
        {
            _logger.LogWarning("Intent extraction failed: {Message}", ex.Message);
            return Result<AssistantIntent>.Failure(new Error("Ai.ProviderError", ex.Message));
        }
    }

    private static JsonNode ToolJson(ToolSpec tool)
    {
        var properties = new JsonObject();
        foreach (var p in tool.Parameters)
        {
            var schema = new JsonObject { ["type"] = "string", ["description"] = p.Description };
            if (p.Allowed is { Count: > 0 } allowed)
            {
                schema["enum"] = new JsonArray(allowed.Select(a => (JsonNode?)a).ToArray());
            }

            properties[p.Name] = schema;
        }

        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = new JsonArray(tool.Parameters.Where(p => p.Required).Select(p => (JsonNode?)p.Name).ToArray()),
                },
            },
        };
    }
}

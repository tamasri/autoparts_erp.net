namespace AutoPartsERP.Application.Common.Abstractions;

/// <summary>What the sender asked for: one of <c>AssistantActions</c> and its arguments as written (not yet matched to records).</summary>
public sealed record AssistantIntent(string Action, IReadOnlyDictionary<string, string> Args)
{
    public string? Arg(string name) => Args.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
}

/// <summary>
/// Turns the sender's text into an <see cref="AssistantIntent"/>. The implementation (a hosted language model) receives the message
/// text only — never data from the database; records are looked up locally after the intent is known.
/// </summary>
public interface IIntentExtractor
{
    bool IsConfigured { get; }

    string ModelName { get; }

    Task<Result<AssistantIntent>> ExtractAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>Runs the rest of the request as another user (the ERP user a WhatsApp number is linked to), with that user's permissions.</summary>
public interface IAssistantIdentity
{
    /// <returns>Failure when the user no longer exists or is locked/deactivated.</returns>
    Task<Result> ActAsAsync(Guid userId, CancellationToken cancellationToken = default);
}

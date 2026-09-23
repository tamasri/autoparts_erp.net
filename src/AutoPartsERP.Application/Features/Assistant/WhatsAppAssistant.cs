using Dapper;
using Microsoft.Extensions.Logging;

namespace AutoPartsERP.Application.Features.Assistant;

/// <summary>Settings for the assistant (section <c>Assistant</c>).</summary>
public sealed class AssistantOptions
{
    /// <summary>Business time zone offset for "today", "this month", overdue days. Syria: +3.</summary>
    public double UtcOffsetHours { get; set; } = 3;

    /// <summary>Messages per linked number per minute.</summary>
    public int MessagesPerMinute { get; set; } = 20;

    /// <summary>How long a one-time link code is valid.</summary>
    public int LinkCodeMinutes { get; set; } = 15;
}

/// <summary>One text message as the WhatsApp gateway received it. <c>FromAlt</c> is the phone-number form when WhatsApp addressed the chat by a private id.</summary>
public sealed record InboundMessage(string MessageId, string From, string? FromAlt, string Text);

/// <summary>
/// The WhatsApp assistant, message by message:
/// <list type="number">
/// <item>Only linked numbers are answered; any other sender gets no reply at all (nothing reveals that the number is an ERP).
/// A number becomes linked when it sends the one-time code an administrator generated for it.</item>
/// <item>The rest of the request runs as the linked ERP user (<see cref="IAssistantIdentity"/>): same permissions, warehouse scope and
/// audit trail as on the screens; a deactivated user loses the assistant at once.</item>
/// <item>The text goes to the language model only to learn the intent (<see cref="IIntentExtractor"/>); with no model configured or
/// on error, keyword rules are used. No database data is ever sent out.</item>
/// <item>Names are matched locally (<see cref="EntityMatcher"/>); unclear matches become a numbered question.</item>
/// </list>
/// </summary>
public sealed class WhatsAppAssistant
{
    public const string FeatureCode = "WHATSAPP_ASSISTANT";

    private static readonly Regex LinkCode = new(@"^\s*(?:ربط|link)?\s*([0-9٠-٩]{6})\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Choice = new(@"^\s*([0-9٠-٩]{1,2})\s*$", RegexOptions.Compiled);

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IAssistantState _state;
    private readonly IAssistantIdentity _identity;
    private readonly ICurrentUser _currentUser;
    private readonly IIntentExtractor _extractor;
    private readonly AssistantAnswers _answers;
    private readonly IManualAuditService _audit;
    private readonly AssistantOptions _options;
    private readonly ILogger<WhatsAppAssistant> _logger;

    public WhatsAppAssistant(
        IDbConnectionFactory connectionFactory, IAssistantState state, IAssistantIdentity identity, ICurrentUser currentUser,
        IIntentExtractor extractor, AssistantAnswers answers, IManualAuditService audit, AssistantOptions options, ILogger<WhatsAppAssistant> logger)
    {
        _connectionFactory = connectionFactory;
        _state = state;
        _identity = identity;
        _currentUser = currentUser;
        _extractor = extractor;
        _answers = answers;
        _audit = audit;
        _options = options;
        _logger = logger;
    }

    private sealed record LinkRow(Guid Id, Guid UserId, string Phone, string Status, string? LinkCodeHash, DateTimeOffset? LinkCodeExpiresAt, int FailedAttempts);

    /// <returns>The reply to send, or null to stay silent.</returns>
    public async Task<string?> HandleAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        var text = (message.Text ?? string.Empty).Trim();
        if (text.Length == 0 || !await _state.FirstSeenAsync(message.MessageId, cancellationToken))
        {
            return null;
        }

        if (text.Length > 500)
        {
            text = text[..500];
        }

        var phones = new[] { PhoneDigits(message.From), PhoneDigits(message.FromAlt) }.Where(p => p is not null).Cast<string>().Distinct().ToArray();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var link = await connection.QuerySingleOrDefaultAsync<LinkRow>(new CommandDefinition(
            """
            SELECT id AS Id, user_id AS UserId, phone AS Phone, status AS Status, link_code_hash AS LinkCodeHash,
                   link_code_expires_at AS LinkCodeExpiresAt, failed_attempts AS FailedAttempts
            FROM assistant_links
            WHERE status = 'ACTIVE' AND (whatsapp_id = ANY(@ids) OR phone = ANY(@phones))
            LIMIT 1;
            """,
            new { ids = new[] { message.From, message.FromAlt ?? message.From }, phones }, cancellationToken: cancellationToken));

        if (link is null)
        {
            return await TryLinkAsync(connection, message, text, phones, cancellationToken);
        }

        if (!await _state.AllowAsync($"assistant:rate:{link.Id}", _options.MessagesPerMinute, TimeSpan.FromMinutes(1), cancellationToken))
        {
            return "رسائل كثيرة خلال دقيقة؛ انتظر قليلاً ثم أعد المحاولة.";
        }

        if ((await _identity.ActAsAsync(link.UserId, cancellationToken)).IsFailure)
        {
            _logger.LogWarning("Assistant: linked user {UserId} is locked or missing; message ignored.", link.UserId);
            return "حسابك في النظام موقوف؛ لا يمكن استخدام المساعد.";
        }

        var enabled = await connection.ExecuteScalarAsync<bool?>(new CommandDefinition(
            "SELECT is_enabled FROM ai_feature_flags WHERE feature_code = @FeatureCode;", new { FeatureCode }, cancellationToken: cancellationToken)) ?? false;
        if (!enabled)
        {
            return "المساعد متوقف حالياً من قبل مدير النظام.";
        }

        if (!_currentUser.HasPermission(PermissionCodes.Assistant.Use))
        {
            return "ليس لديك صلاحية استخدام المساعد.";
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE assistant_links SET last_used_at = now() WHERE id = @Id;", new { link.Id }, cancellationToken: cancellationToken));

        var (reply, action) = await AnswerAsync(link.Id, text, cancellationToken);

        await _audit.LogAsync(new ManualAuditEntry(
            _currentUser.CorrelationId, AuditActions.AssistantQuery, "ASSISTANT", "WhatsApp", link.Id, _currentUser.UserId, _currentUser.Username,
            action, "SUCCESS", ReasonNotes: text.Length > 200 ? text[..200] : text), cancellationToken);
        return reply;
    }

    private async Task<(string Reply, string Action)> AnswerAsync(Guid linkId, string text, CancellationToken ct)
    {
        var pending = await _state.GetPendingAsync(linkId, ct);
        if (pending is not null && Choice.Match(text) is { Success: true } picked)
        {
            var n = int.Parse(AssistantAnswers.LatinDigits(picked.Groups[1].Value), System.Globalization.CultureInfo.InvariantCulture);
            await _state.ClearPendingAsync(linkId, ct);
            if (n == 0)
            {
                return ("أُلغي الطلب.", "cancel");
            }

            if (n < 1 || n > pending.Options.Count)
            {
                return ($"اختر رقماً بين 1 و{pending.Options.Count}. أعد السؤال من جديد.", "choice_invalid");
            }

            var chosen = await _answers.AnswerAsync(new AssistantIntent(pending.Action, pending.Args), pending.Options[n - 1].Id, ct);
            return (chosen.Text, pending.Action);
        }

        if (pending is not null)
        {
            await _state.ClearPendingAsync(linkId, ct); // a new question replaces the unanswered choice
        }

        var intent = await ExtractAsync(text, ct);
        var reply = await _answers.AnswerAsync(intent, null, ct);
        if (reply.Pending is not null)
        {
            await _state.SetPendingAsync(linkId, reply.Pending, ct);
        }

        return (reply.Text, intent.Action);
    }

    private async Task<AssistantIntent> ExtractAsync(string text, CancellationToken ct)
    {
        if (_extractor.IsConfigured)
        {
            var extracted = await _extractor.ExtractAsync(text, ct);
            if (extracted.IsSuccess)
            {
                return extracted.Value!;
            }

            _logger.LogWarning("Assistant: intent extraction failed ({Error}); using keyword rules.", extracted.Error.Message);
        }

        return RuleBasedIntents.Parse(text);
    }

    /// <summary>
    /// An unknown sender is answered only when it sends the valid one-time code of a pending link for its own number (or, when WhatsApp
    /// hides the number behind a private id, of any pending link — the code, its 15-minute life and 5 tries per link and per sender
    /// make guessing impractical). Everything else from unknown senders is ignored silently.
    /// </summary>
    private async Task<string?> TryLinkAsync(IDbConnection connection, InboundMessage message, string text, string[] phones, CancellationToken ct)
    {
        if (LinkCode.Match(text) is not { Success: true } m)
        {
            return null;
        }

        if (!await _state.AllowAsync($"assistant:linktry:{message.From}", 5, TimeSpan.FromHours(1), ct))
        {
            return null;
        }

        var code = AssistantAnswers.LatinDigits(m.Groups[1].Value);
        var candidates = (await connection.QueryAsync<LinkRow>(new CommandDefinition(
            """
            SELECT id AS Id, user_id AS UserId, phone AS Phone, status AS Status, link_code_hash AS LinkCodeHash,
                   link_code_expires_at AS LinkCodeExpiresAt, failed_attempts AS FailedAttempts
            FROM assistant_links
            WHERE status = 'PENDING' AND link_code_expires_at > now() AND failed_attempts < 5
              AND (phone = ANY(@phones) OR cardinality(@phones) = 0);
            """,
            new { phones }, cancellationToken: ct))).ToList();

        var hash = AssistantLinks.HashCode(code);
        var match = candidates.FirstOrDefault(c => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(c.LinkCodeHash ?? string.Empty), Encoding.ASCII.GetBytes(hash)));
        if (match is null)
        {
            if (candidates.Count > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE assistant_links SET failed_attempts = failed_attempts + 1 WHERE id = ANY(@ids);",
                    new { ids = candidates.Select(c => c.Id).ToArray() }, cancellationToken: ct));
            }

            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE assistant_links
            SET status = 'ACTIVE', whatsapp_id = @From, verified_at = now(), link_code_hash = NULL, link_code_expires_at = NULL
            WHERE id = @Id AND status = 'PENDING';
            """,
            new { match.Id, message.From }, cancellationToken: ct));

        await _audit.LogAsync(new ManualAuditEntry(
            Guid.NewGuid(), AuditActions.AssistantLinkVerified, "ASSISTANT", "AssistantLink", match.Id, match.UserId, string.Empty,
            "LINK_VERIFIED", "SUCCESS", ReasonNotes: $"+{match.Phone}"), ct);
        return "✅ تم ربط هذا الرقم بحسابك في النظام.\n\n" + AssistantAnswers.HelpText;
    }

    /// <summary>"963912345678@s.whatsapp.net" or "963912345678:12@s.whatsapp.net" → "963912345678"; private ids (@lid) and groups → null.</summary>
    public static string? PhoneDigits(string? jid)
    {
        if (string.IsNullOrWhiteSpace(jid) || !jid.EndsWith("@s.whatsapp.net", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var user = jid[..jid.IndexOf('@')];
        var colon = user.IndexOf(':');
        if (colon >= 0)
        {
            user = user[..colon];
        }

        return user.Length is >= 8 and <= 15 && user.All(char.IsAsciiDigit) ? user : null;
    }
}

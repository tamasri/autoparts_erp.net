using AutoPartsERP.Contracts.Assistant;
using Dapper;

namespace AutoPartsERP.Application.Features.Assistant;

/// <summary>Link codes: six random digits, stored only as a SHA-256 hash, valid for a few minutes, five wrong tries at most.</summary>
public static class AssistantLinks
{
    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    public static string HashCode(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("assistant-link:" + code)));

    /// <summary>International form, digits only: "+963 933-123-456" / "00963933123456" → "963933123456". Local numbers (leading 0) are refused.</summary>
    public static Result<string> NormalizePhone(string? raw)
    {
        var digits = new string(AssistantAnswers.LatinDigits(raw ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }

        if (digits.StartsWith('0') || digits.Length is < 8 or > 15)
        {
            return Result<string>.Failure(new Error("Validation.Phone", "أدخل الرقم بالصيغة الدولية مع رمز البلد، مثل 963933123456."));
        }

        return Result<string>.Success(digits);
    }
}

public sealed record ListAssistantLinksQuery : IRequest<Result<IReadOnlyList<AssistantLinkDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Assistant.Manage;
}

public sealed class ListAssistantLinksQueryHandler : IRequestHandler<ListAssistantLinksQuery, Result<IReadOnlyList<AssistantLinkDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ListAssistantLinksQueryHandler(IDbConnectionFactory connectionFactory) => _connectionFactory = connectionFactory;

    public async Task<Result<IReadOnlyList<AssistantLinkDto>>> Handle(ListAssistantLinksQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        // "Has access" = the user holds assistant:use through one of their roles (the assistant refuses the others with a clear message).
        var rows = await connection.QueryAsync<AssistantLinkDto>(new CommandDefinition(
            """
            SELECT l.id AS Id, l.user_id AS UserId, u.user_name AS UserName, COALESCE(u.full_name, u.user_name) AS FullName, l.phone AS Phone,
                   l.status AS Status,
                   EXISTS (SELECT 1 FROM asp_net_user_roles ur INNER JOIN asp_net_role_claims rc ON rc.role_id = ur.role_id
                           WHERE ur.user_id = l.user_id AND rc.claim_type = 'permission' AND rc.claim_value = 'assistant:use') AS UserHasAccess,
                   l.created_at AS CreatedAt, l.verified_at AS VerifiedAt, l.last_used_at AS LastUsedAt, l.link_code_expires_at AS CodeExpiresAt
            FROM assistant_links l
            INNER JOIN asp_net_users u ON u.id = l.user_id
            ORDER BY (l.status = 'REVOKED'), l.created_at DESC;
            """,
            cancellationToken: cancellationToken));
        return Result<IReadOnlyList<AssistantLinkDto>>.Success(rows.ToList());
    }
}

public sealed record CreateAssistantLinkCommand(Guid UserId, string Phone)
    : IRequest<Result<AssistantLinkCodeDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Assistant.Manage;
    public string AuditModule => "ASSISTANT";
}

public sealed class CreateAssistantLinkCommandValidator : AbstractValidator<CreateAssistantLinkCommand>
{
    public CreateAssistantLinkCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Phone).NotEmpty();
    }
}

public sealed class CreateAssistantLinkCommandHandler : IRequestHandler<CreateAssistantLinkCommand, Result<AssistantLinkCodeDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly AssistantOptions _options;

    public CreateAssistantLinkCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, AssistantOptions options)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _options = options;
    }

    public async Task<Result<AssistantLinkCodeDto>> Handle(CreateAssistantLinkCommand request, CancellationToken cancellationToken)
    {
        var phone = AssistantLinks.NormalizePhone(request.Phone);
        if (phone.IsFailure)
        {
            return Result<AssistantLinkCodeDto>.Failure(phone.Error);
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var userOk = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM asp_net_users WHERE id = @UserId AND (lockout_end IS NULL OR lockout_end < now()));",
            new { request.UserId }, cancellationToken: cancellationToken));
        if (!userOk)
        {
            return Result<AssistantLinkCodeDto>.Failure(new Error("Assistant.UserNotFound", "المستخدم غير موجود أو موقوف."));
        }

        var taken = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM assistant_links WHERE phone = @Phone AND status <> 'REVOKED');",
            new { Phone = phone.Value }, cancellationToken: cancellationToken));
        if (taken)
        {
            return Result<AssistantLinkCodeDto>.Failure(new Error("Assistant.PhoneConflict", "هذا الرقم مربوط أو قيد الربط؛ ألغِ الربط الحالي أولاً."));
        }

        var code = AssistantLinks.NewCode();
        var expires = DateTimeOffset.UtcNow.AddMinutes(_options.LinkCodeMinutes);
        var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO assistant_links (user_id, phone, status, link_code_hash, link_code_expires_at, created_by)
            VALUES (@UserId, @Phone, 'PENDING', @Hash, @Expires, @By) RETURNING id;
            """,
            new { request.UserId, Phone = phone.Value, Hash = AssistantLinks.HashCode(code), Expires = expires, By = _currentUser.UserId },
            cancellationToken: cancellationToken));

        return Result<AssistantLinkCodeDto>.Success(new AssistantLinkCodeDto(id, phone.Value!, code, expires));
    }
}

/// <summary>A fresh code for a link that is still waiting (the first one expired or was mistyped five times).</summary>
public sealed record RenewAssistantLinkCodeCommand(Guid LinkId)
    : IRequest<Result<AssistantLinkCodeDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Assistant.Manage;
    public string AuditModule => "ASSISTANT";
}

public sealed class RenewAssistantLinkCodeCommandHandler : IRequestHandler<RenewAssistantLinkCodeCommand, Result<AssistantLinkCodeDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly AssistantOptions _options;

    public RenewAssistantLinkCodeCommandHandler(IDbConnectionFactory connectionFactory, AssistantOptions options)
    {
        _connectionFactory = connectionFactory;
        _options = options;
    }

    public async Task<Result<AssistantLinkCodeDto>> Handle(RenewAssistantLinkCodeCommand request, CancellationToken cancellationToken)
    {
        var code = AssistantLinks.NewCode();
        var expires = DateTimeOffset.UtcNow.AddMinutes(_options.LinkCodeMinutes);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var phone = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            UPDATE assistant_links SET link_code_hash = @Hash, link_code_expires_at = @Expires, failed_attempts = 0
            WHERE id = @LinkId AND status = 'PENDING' RETURNING phone;
            """,
            new { request.LinkId, Hash = AssistantLinks.HashCode(code), Expires = expires }, cancellationToken: cancellationToken));
        return phone is null
            ? Result<AssistantLinkCodeDto>.Failure(new Error("Assistant.NotPending", "هذا الربط ليس قيد الانتظار."))
            : Result<AssistantLinkCodeDto>.Success(new AssistantLinkCodeDto(request.LinkId, phone, code, expires));
    }
}

public sealed record RevokeAssistantLinkCommand(Guid LinkId)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Assistant.Manage;
    public string AuditModule => "ASSISTANT";
}

public sealed class RevokeAssistantLinkCommandHandler : IRequestHandler<RevokeAssistantLinkCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public RevokeAssistantLinkCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(RevokeAssistantLinkCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var changed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE assistant_links SET status = 'REVOKED', revoked_at = now(), revoked_by = @By, link_code_hash = NULL, link_code_expires_at = NULL
            WHERE id = @LinkId AND status <> 'REVOKED';
            """,
            new { request.LinkId, By = _currentUser.UserId }, cancellationToken: cancellationToken));
        return changed == 0
            ? Result<Guid>.Failure(new Error("Assistant.NotFound", "الربط غير موجود أو ملغى مسبقاً."))
            : Result<Guid>.Success(request.LinkId);
    }
}

public sealed record GetAssistantStatusQuery : IRequest<Result<AssistantStatusDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Assistant.Manage;
}

public sealed class GetAssistantStatusQueryHandler : IRequestHandler<GetAssistantStatusQuery, Result<AssistantStatusDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IAssistantState _state;
    private readonly IIntentExtractor _extractor;

    public GetAssistantStatusQueryHandler(IDbConnectionFactory connectionFactory, IAssistantState state, IIntentExtractor extractor)
    {
        _connectionFactory = connectionFactory;
        _state = state;
        _extractor = extractor;
    }

    public async Task<Result<AssistantStatusDto>> Handle(GetAssistantStatusQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var enabled = await connection.ExecuteScalarAsync<bool?>(new CommandDefinition(
            "SELECT is_enabled FROM ai_feature_flags WHERE feature_code = @Code;", new { Code = WhatsAppAssistant.FeatureCode }, cancellationToken: cancellationToken)) ?? false;
        var gateway = await _state.GetGatewayStatusAsync(cancellationToken);

        // A report older than two minutes means the gateway is not running (it reports every minute).
        var stale = gateway is null || gateway.ReportedAt < DateTimeOffset.UtcNow.AddMinutes(-2);
        return Result<AssistantStatusDto>.Success(new AssistantStatusDto(
            enabled, _extractor.IsConfigured, _extractor.ModelName,
            stale ? "offline" : gateway!.State, stale ? null : gateway!.Qr, gateway?.Account, gateway?.ReportedAt));
    }
}

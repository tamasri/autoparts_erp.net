namespace AutoPartsERP.Application.Common.Abstractions;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid UserId { get; }

    string Username { get; }

    string FullName { get; }

    Guid CorrelationId { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }

    IReadOnlyCollection<string> Roles { get; }

    bool HasPermission(string code);

    bool HasRole(string roleCode);
}

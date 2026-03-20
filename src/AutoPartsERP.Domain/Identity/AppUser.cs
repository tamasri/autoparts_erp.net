using AutoPartsERP.Domain.Common;

namespace AutoPartsERP.Domain.Identity;

public sealed class AppUser : AuditableEntity
{
    private readonly List<AppUserRole> _roleAssignments = new();

    public AppUser(
        Guid id,
        string userName,
        string email,
        string firstName,
        string lastName,
        string? passwordHash = null)
        : base(id)
    {
        UserName = userName.Trim();
        NormalizedUserName = Normalize(userName);
        Email = email.Trim();
        NormalizedEmail = Normalize(email);
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        PasswordHash = passwordHash;
        SecurityStamp = Guid.NewGuid().ToString("N");
        ConcurrencyStamp = Guid.NewGuid().ToString("N");
        IsActive = true;
    }

    public string UserName { get; private set; }

    public string NormalizedUserName { get; private set; }

    public string Email { get; private set; }

    public string NormalizedEmail { get; private set; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public string? PasswordHash { get; private set; }

    public string SecurityStamp { get; private set; }

    public string ConcurrencyStamp { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsLockedOut => LockoutEndUtc.HasValue && LockoutEndUtc.Value > DateTimeOffset.UtcNow;

    public DateTimeOffset? LockoutEndUtc { get; private set; }

    public DateTimeOffset? LastLoginAtUtc { get; private set; }

    public IReadOnlyCollection<AppUserRole> RoleAssignments => _roleAssignments.AsReadOnly();

    public string FullName => string.Concat(FirstName, " ", LastName).Trim();

    public void UpdateProfile(string firstName, string lastName, string email)
    {
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Email = email.Trim();
        NormalizedEmail = Normalize(email);
        TouchSecurityState();
    }

    public void UpdateUserName(string userName)
    {
        UserName = userName.Trim();
        NormalizedUserName = Normalize(userName);
        TouchSecurityState();
    }

    public void SetPasswordHash(string passwordHash)
    {
        PasswordHash = passwordHash;
        TouchSecurityState();
    }

    public void RecordSuccessfulLogin()
    {
        LastLoginAtUtc = DateTimeOffset.UtcNow;
        LockoutEndUtc = null;
        Touch();
    }

    public void LockUntil(DateTimeOffset lockoutEndUtc)
    {
        LockoutEndUtc = lockoutEndUtc;
        Touch();
    }

    public void Unlock()
    {
        LockoutEndUtc = null;
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        TouchSecurityState();
    }

    public void Deactivate()
    {
        IsActive = false;
        TouchSecurityState();
    }

    public void AssignRole(Guid appRoleId, Guid? assignedByUserId = null)
    {
        if (_roleAssignments.Any(role => role.AppRoleId == appRoleId))
        {
            return;
        }

        _roleAssignments.Add(new AppUserRole(Guid.NewGuid(), Id, appRoleId, assignedByUserId));
        Touch();
    }

    public void RemoveRole(Guid appRoleId)
    {
        var assignment = _roleAssignments.FirstOrDefault(role => role.AppRoleId == appRoleId);

        if (assignment is null)
        {
            return;
        }

        _roleAssignments.Remove(assignment);
        Touch();
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private void TouchSecurityState()
    {
        SecurityStamp = Guid.NewGuid().ToString("N");
        ConcurrencyStamp = Guid.NewGuid().ToString("N");
        Touch();
    }
}

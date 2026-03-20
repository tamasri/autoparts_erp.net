namespace AutoPartsERP.Infrastructure.Identity;

public sealed class AppUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;

    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? CreatedBy { get; set; }
}
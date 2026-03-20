namespace AutoPartsERP.Infrastructure.Identity;

public sealed class AppRole : IdentityRole<Guid>
{
    public bool IsSystem { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid CreatedBy { get; set; }
}
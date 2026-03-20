using AutoPartsERP.Domain.Common;

namespace AutoPartsERP.Domain.Identity;

public sealed class AppUserRole : AuditableEntity
{
    public AppUserRole(Guid id, Guid appUserId, Guid appRoleId, Guid? assignedByUserId = null)
        : base(id)
    {
        AppUserId = appUserId;
        AppRoleId = appRoleId;
        AssignedByUserId = assignedByUserId;
        AssignedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid AppUserId { get; }

    public Guid AppRoleId { get; }

    public Guid? AssignedByUserId { get; }

    public DateTimeOffset AssignedAtUtc { get; }
}

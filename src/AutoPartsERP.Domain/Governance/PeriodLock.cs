using AutoPartsERP.Domain.Common;

namespace AutoPartsERP.Domain.Governance;

public sealed class PeriodLock : AuditableEntity
{
    public PeriodLock(
        Guid id,
        string periodKey,
        string moduleCode,
        Guid lockedByUserId,
        string reason)
        : base(id)
    {
        PeriodKey = periodKey.Trim();
        ModuleCode = moduleCode.Trim();
        LockedByUserId = lockedByUserId;
        Reason = reason.Trim();
        IsLocked = true;
        LockedAtUtc = DateTimeOffset.UtcNow;
    }

    public string PeriodKey { get; }

    public string ModuleCode { get; }

    public string Reason { get; private set; }

    public bool IsLocked { get; private set; }

    public Guid LockedByUserId { get; private set; }

    public DateTimeOffset LockedAtUtc { get; private set; }

    public Guid? UnlockedByUserId { get; private set; }

    public DateTimeOffset? UnlockedAtUtc { get; private set; }

    public void Relock(Guid lockedByUserId, string reason)
    {
        IsLocked = true;
        LockedByUserId = lockedByUserId;
        LockedAtUtc = DateTimeOffset.UtcNow;
        UnlockedByUserId = null;
        UnlockedAtUtc = null;
        Reason = reason.Trim();
        Touch();
    }

    public void Unlock(Guid unlockedByUserId, string reason)
    {
        IsLocked = false;
        UnlockedByUserId = unlockedByUserId;
        UnlockedAtUtc = DateTimeOffset.UtcNow;
        Reason = reason.Trim();
        Touch();
    }
}

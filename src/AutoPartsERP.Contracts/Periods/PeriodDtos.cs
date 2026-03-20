namespace AutoPartsERP.Contracts.Periods;

public sealed record PeriodLockDto(
    Guid Id,
    string PeriodKey,
    string ModuleCode,
    string Reason,
    bool IsLocked,
    Guid LockedByUserId,
    DateTimeOffset LockedAtUtc,
    Guid? UnlockedByUserId,
    DateTimeOffset? UnlockedAtUtc);

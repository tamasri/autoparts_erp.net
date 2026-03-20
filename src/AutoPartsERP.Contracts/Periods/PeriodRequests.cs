namespace AutoPartsERP.Contracts.Periods;

public sealed record LockPeriodRequest(string PeriodKey, string ModuleCode, string Reason);

public sealed record UnlockPeriodRequest(string Reason);

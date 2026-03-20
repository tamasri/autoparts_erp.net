namespace AutoPartsERP.Application.Common.Models;

public sealed record PeriodLockFilter(int? Year = null, int? Month = null, string? ModuleCode = null);

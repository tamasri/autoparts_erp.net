namespace AutoPartsERP.Contracts.Kpi;

public sealed record CreateKpiDefinitionRequest(
    string Key,
    string Domain,
    string Title,
    string TitleAr,
    string Unit,
    string Direction,
    string? Description);

public sealed record SetKpiThresholdRequest(
    decimal? WarningValue,
    decimal? CriticalValue,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Reason);

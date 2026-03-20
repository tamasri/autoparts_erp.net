namespace AutoPartsERP.Contracts.Kpi;

public sealed record KpiDefinitionDto(
    Guid Id,
    string Key,
    string Domain,
    string Title,
    string TitleAr,
    string Unit,
    string Direction,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record KpiThresholdDto(
    Guid Id,
    Guid KpiDefinitionId,
    decimal? WarningValue,
    decimal? CriticalValue,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    Guid SetBy,
    DateTimeOffset CreatedAt);

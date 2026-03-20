namespace AutoPartsERP.Domain.Operational;

public sealed class KpiThreshold : AuditableEntity
{
    private KpiThreshold(
        Guid id,
        Guid kpiDefinitionId,
        decimal? warningValue,
        decimal? criticalValue,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        Guid setBy)
        : base(id)
    {
        KpiDefinitionId = kpiDefinitionId;
        WarningValue = warningValue;
        CriticalValue = criticalValue;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        SetBy = setBy;
    }

    private KpiThreshold()
        : base(Guid.NewGuid())
    {
    }

    public Guid KpiDefinitionId { get; private set; }

    public decimal? WarningValue { get; private set; }

    public decimal? CriticalValue { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public Guid SetBy { get; private set; }

    public static Result<KpiThreshold> Create(
        Guid kpiDefinitionId,
        decimal? warningValue,
        decimal? criticalValue,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        Guid setBy)
    {
        if (kpiDefinitionId == Guid.Empty)
        {
            return Result<KpiThreshold>.Failure(new Error("Kpi.DefinitionRequired", "KPI definition is required."));
        }

        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)
        {
            return Result<KpiThreshold>.Failure(new Error("Kpi.EffectiveRangeInvalid", "Effective date range is invalid."));
        }

        return Result<KpiThreshold>.Success(new KpiThreshold(
            Guid.NewGuid(),
            kpiDefinitionId,
            warningValue,
            criticalValue,
            effectiveFrom,
            effectiveTo,
            setBy));
    }
}

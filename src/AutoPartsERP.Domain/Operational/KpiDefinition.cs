namespace AutoPartsERP.Domain.Operational;

public sealed class KpiDefinition : AuditableEntity
{
    private KpiDefinition(
        Guid id,
        string key,
        string domain,
        string title,
        string titleAr,
        string unit,
        string direction,
        string? description,
        Guid createdBy)
        : base(id)
    {
        Key = key;
        Domain = domain;
        Title = title;
        TitleAr = titleAr;
        Unit = unit;
        Direction = direction;
        Description = description;
        IsActive = true;
        CreatedBy = createdBy;
    }

    private KpiDefinition()
        : base(Guid.NewGuid())
    {
    }

    public string Key { get; private set; } = string.Empty;

    public string Domain { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string TitleAr { get; private set; } = string.Empty;

    public string Unit { get; private set; } = string.Empty;

    public string Direction { get; private set; } = "UP";

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public Guid CreatedBy { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static Result<KpiDefinition> Create(
        string key,
        string domain,
        string title,
        string titleAr,
        string unit,
        string direction,
        string? description,
        Guid createdBy)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Result<KpiDefinition>.Failure(new Error("Kpi.KeyRequired", "KPI key is required."));
        }

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(titleAr))
        {
            return Result<KpiDefinition>.Failure(new Error("Kpi.TitleRequired", "KPI title and Arabic title are required."));
        }

        return Result<KpiDefinition>.Success(new KpiDefinition(
            Guid.NewGuid(),
            key.Trim().ToLowerInvariant(),
            domain.Trim().ToUpperInvariant(),
            title.Trim(),
            titleAr.Trim(),
            unit.Trim().ToUpperInvariant(),
            direction.Trim().ToUpperInvariant(),
            description?.Trim(),
            createdBy));
    }
}

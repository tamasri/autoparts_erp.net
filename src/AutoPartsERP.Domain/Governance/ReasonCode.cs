using AutoPartsERP.Domain.Common;

namespace AutoPartsERP.Domain.Governance;

public sealed class ReasonCode : AuditableEntity
{
    private ReasonCode() : base(Guid.Empty) { }

    public ReasonCode(
        Guid id,
        string category,
        string code,
        string description,
        bool requiresComment,
        string? appliesTo = null)
        : base(id)
    {
        Category = category.Trim();
        Code = Normalize(code);
        Description = description.Trim();
        RequiresComment = requiresComment;
        AppliesTo = string.IsNullOrWhiteSpace(appliesTo) ? null : appliesTo.Trim();
        IsActive = true;
    }

    public string Category { get; private set; }

    public string Code { get; private set; }

    public string Description { get; private set; }

    public bool RequiresComment { get; private set; }

    public string? AppliesTo { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string category, string description, bool requiresComment, string? appliesTo = null)
    {
        Category = category.Trim();
        Description = description.Trim();
        RequiresComment = requiresComment;
        AppliesTo = string.IsNullOrWhiteSpace(appliesTo) ? null : appliesTo.Trim();
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        Touch();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToUpperInvariant();
    }
}

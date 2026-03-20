namespace AutoPartsERP.Domain.Operational.ValueObjects;

public sealed record CategoryPath
{
    public CategoryPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Category path is required.", nameof(value));
        }

        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public bool IsDescendantOf(string parentPath)
    {
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return false;
        }

        var normalized = parentPath.Trim().ToLowerInvariant();
        return Value == normalized || Value.StartsWith($"{normalized}.", StringComparison.Ordinal);
    }

    public override string ToString() => Value;
}

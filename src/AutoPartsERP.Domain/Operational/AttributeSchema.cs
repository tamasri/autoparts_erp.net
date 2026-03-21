namespace AutoPartsERP.Domain.Operational;

public sealed class AttributeSchema : AuditableEntity
{
    private AttributeSchema() : base(Guid.Empty) { }

    private static readonly HashSet<string> AllowedDataTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TEXT",
        "NUMBER",
        "BOOLEAN",
        "SELECT"
    };

    private AttributeSchema(
        Guid id,
        Guid categoryId,
        string code,
        string label,
        string? labelAr,
        string dataType,
        bool isRequired,
        bool isFilterable,
        int sortOrder,
        string optionsJson)
        : base(id)
    {
        CategoryId = categoryId;
        Code = code;
        Label = label;
        LabelAr = labelAr;
        DataType = dataType;
        IsRequired = isRequired;
        IsFilterable = isFilterable;
        SortOrder = sortOrder;
        OptionsJson = optionsJson;
    }

    public Guid CategoryId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Label { get; private set; } = string.Empty;

    public string? LabelAr { get; private set; }

    public string DataType { get; private set; } = "TEXT";

    public bool IsRequired { get; private set; }

    public bool IsFilterable { get; private set; }

    public int SortOrder { get; private set; }

    public string OptionsJson { get; private set; } = "{}";

    public static Result<AttributeSchema> Create(
        Guid categoryId,
        string code,
        string label,
        string? labelAr,
        string dataType,
        bool isRequired,
        bool isFilterable,
        int sortOrder,
        string optionsJson = "{}")
    {
        if (categoryId == Guid.Empty)
        {
            return Result<AttributeSchema>.Failure(new Error("AttributeSchema.CategoryRequired", "Category is required."));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Result<AttributeSchema>.Failure(new Error("AttributeSchema.CodeRequired", "Code is required."));
        }

        if (!AllowedDataTypes.Contains(dataType))
        {
            return Result<AttributeSchema>.Failure(new Error("AttributeSchema.InvalidType", "Unsupported attribute data type."));
        }

        return Result<AttributeSchema>.Success(new AttributeSchema(
            Guid.NewGuid(),
            categoryId,
            code.Trim().ToUpperInvariant(),
            label.Trim(),
            labelAr?.Trim(),
            dataType.Trim().ToUpperInvariant(),
            isRequired,
            isFilterable,
            sortOrder,
            string.IsNullOrWhiteSpace(optionsJson) ? "{}" : optionsJson));
    }
}

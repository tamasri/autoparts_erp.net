namespace AutoPartsERP.Domain.Wms;

public sealed class Item : AuditableEntity
{
    private readonly List<ItemAlias> _aliases = new();
    private readonly List<ItemInterchange> _interchanges = new();
    private readonly List<ItemReorderSetting> _reorderSettings = new();

    private Item(
        Guid id,
        Guid? skuId,
        string partNumber,
        string partNumberCanonical,
        string partNumberNumeric,
        string nameEn,
        string nameAr,
        string? nameArColloquial,
        string? brand,
        string? categoryPath,
        bool hasWarranty,
        int warrantyMonths,
        bool isBatchTracked,
        decimal reorderLevel,
        Guid createdBy)
        : base(id)
    {
        SkuId = skuId;
        PartNumber = partNumber;
        PartNumberCanonical = partNumberCanonical;
        PartNumberNumeric = partNumberNumeric;
        NameEn = nameEn;
        NameAr = nameAr;
        NameArColloquial = nameArColloquial;
        Brand = brand;
        CategoryPath = categoryPath;
        UnitOfMeasure = "PIECE";
        HasWarranty = hasWarranty;
        WarrantyMonths = warrantyMonths;
        IsBatchTracked = isBatchTracked;
        ReorderLevel = reorderLevel;
        IsActive = true;
        CreatedBy = createdBy;
    }

    private Item()
        : base(Guid.NewGuid())
    {
    }

    public Guid? SkuId { get; private set; }

    public string PartNumber { get; private set; } = string.Empty;

    public string PartNumberCanonical { get; private set; } = string.Empty;

    public string PartNumberNumeric { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string? NameArColloquial { get; private set; }

    public string? Brand { get; private set; }

    public string? CategoryPath { get; private set; }

    public string UnitOfMeasure { get; private set; } = "PIECE";

    public bool HasWarranty { get; private set; }

    public int WarrantyMonths { get; private set; }

    public bool IsBatchTracked { get; private set; }

    public decimal ReorderLevel { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsStopShip { get; private set; }

    public string? StopShipReason { get; private set; }

    public string? Notes { get; private set; }

    public Guid CreatedBy { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public IReadOnlyCollection<ItemAlias> Aliases => _aliases.AsReadOnly();

    public IReadOnlyCollection<ItemInterchange> Interchanges => _interchanges.AsReadOnly();

    public IReadOnlyCollection<ItemReorderSetting> ReorderSettings => _reorderSettings.AsReadOnly();

    public static Result<Item> Create(
        Guid? skuId,
        string partNumber,
        string nameEn,
        string nameAr,
        string? nameArColloquial,
        string? brand,
        string? categoryPath,
        bool hasWarranty,
        int warrantyMonths,
        bool isBatchTracked,
        decimal reorderLevel,
        IPartNumberService partNumberService,
        Guid createdBy)
    {
        if (string.IsNullOrWhiteSpace(partNumber))
        {
            return Result<Item>.Failure(new Error("Item.PartNumberRequired", "Part number is required."));
        }

        if (string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameAr))
        {
            return Result<Item>.Failure(new Error("Item.NameRequired", "English and Arabic names are required."));
        }

        if (reorderLevel < 0)
        {
            return Result<Item>.Failure(new Error("Item.ReorderLevelInvalid", "Reorder level cannot be negative."));
        }

        if (hasWarranty && warrantyMonths <= 0)
        {
            return Result<Item>.Failure(new Error("Item.WarrantyMonthsRequired", "Warranty months must be greater than zero when warranty is enabled."));
        }

        var normalized = partNumberService.NormalizePartNumber(partNumber);
        if (string.IsNullOrWhiteSpace(normalized.Canonical))
        {
            return Result<Item>.Failure(new Error("Item.PartNumberInvalid", "Part number normalization produced an empty canonical value."));
        }

        var item = new Item(
            Guid.NewGuid(),
            skuId,
            partNumber.Trim(),
            normalized.Canonical,
            normalized.Numeric,
            nameEn.Trim(),
            nameAr.Trim(),
            string.IsNullOrWhiteSpace(nameArColloquial) ? null : nameArColloquial.Trim(),
            string.IsNullOrWhiteSpace(brand) ? null : brand.Trim(),
            string.IsNullOrWhiteSpace(categoryPath) ? null : categoryPath.Trim(),
            hasWarranty,
            warrantyMonths,
            isBatchTracked,
            reorderLevel,
            createdBy);

        return Result<Item>.Success(item);
    }

    public Result Update(
        string nameEn,
        string nameAr,
        string? nameArColloquial,
        string? brand,
        string? categoryPath,
        bool isActive,
        bool hasWarranty,
        int warrantyMonths,
        bool isBatchTracked,
        decimal reorderLevel,
        string? notes,
        Guid by)
    {
        if (string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameAr))
        {
            return Result.Failure(new Error("Item.NameRequired", "English and Arabic names are required."));
        }

        if (reorderLevel < 0)
        {
            return Result.Failure(new Error("Item.ReorderLevelInvalid", "Reorder level cannot be negative."));
        }

        if (hasWarranty && warrantyMonths <= 0)
        {
            return Result.Failure(new Error("Item.WarrantyMonthsRequired", "Warranty months must be greater than zero when warranty is enabled."));
        }

        NameEn = nameEn.Trim();
        NameAr = nameAr.Trim();
        NameArColloquial = string.IsNullOrWhiteSpace(nameArColloquial) ? null : nameArColloquial.Trim();
        Brand = string.IsNullOrWhiteSpace(brand) ? null : brand.Trim();
        CategoryPath = string.IsNullOrWhiteSpace(categoryPath) ? null : categoryPath.Trim();
        IsActive = isActive;
        HasWarranty = hasWarranty;
        WarrantyMonths = warrantyMonths;
        IsBatchTracked = isBatchTracked;
        ReorderLevel = reorderLevel;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedBy = by;
        Touch();

        return Result.Success();
    }

    public Result MarkStopShip(string reason, Guid by)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(new Error("Item.StopShipReasonRequired", "Stop ship reason is required."));
        }

        IsStopShip = true;
        StopShipReason = reason.Trim();
        UpdatedBy = by;
        Touch();
        return Result.Success();
    }

    public Result AddAlias(string alias, string source, IPartNumberService partNumberService)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return Result.Failure(new Error("Item.AliasRequired", "Alias is required."));
        }

        var normalized = partNumberService.NormalizePartNumber(alias);
        if (string.IsNullOrWhiteSpace(normalized.Canonical))
        {
            return Result.Failure(new Error("Item.AliasInvalid", "Alias normalization produced an empty canonical value."));
        }

        var duplicate = _aliases.Any(x => string.Equals(x.AliasCanonical, normalized.Canonical, StringComparison.Ordinal));
        if (duplicate)
        {
            return Result.Failure(new Error("Item.AliasDuplicate", "Alias canonical value already exists for this item."));
        }

        _aliases.Add(new ItemAlias(Guid.NewGuid(), Id, alias.Trim(), normalized.Canonical, source));
        Touch();
        return Result.Success();
    }

    public Result AddInterchange(Guid interchangeItemId, string type, int priority, Guid by)
    {
        if (interchangeItemId == Guid.Empty)
        {
            return Result.Failure(new Error("Item.InterchangeItemRequired", "Interchange item is required."));
        }

        if (interchangeItemId == Id)
        {
            return Result.Failure(new Error("Item.InterchangeSelf", "An item cannot be interchangeable with itself."));
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            return Result.Failure(new Error("Item.InterchangeTypeRequired", "Interchange type is required."));
        }

        if (_interchanges.Any(x => x.InterchangeItemId == interchangeItemId))
        {
            return Result.Failure(new Error("Item.InterchangeDuplicate", "Interchange already exists for this item."));
        }

        _interchanges.Add(new ItemInterchange(Guid.NewGuid(), Id, interchangeItemId, type.Trim().ToUpperInvariant(), priority <= 0 ? 1 : priority, by));
        Touch();
        return Result.Success();
    }
}

public sealed class ItemAlias : AuditableEntity
{
    public ItemAlias(Guid id, Guid itemId, string alias, string aliasCanonical, string source)
        : base(id)
    {
        ItemId = itemId;
        Alias = alias;
        AliasCanonical = aliasCanonical;
        Source = string.IsNullOrWhiteSpace(source) ? "MANUAL" : source.Trim().ToUpperInvariant();
    }

    private ItemAlias()
        : base(Guid.NewGuid())
    {
    }

    public Guid ItemId { get; private set; }

    public string Alias { get; private set; } = string.Empty;

    public string AliasCanonical { get; private set; } = string.Empty;

    public string Source { get; private set; } = "MANUAL";
}

public sealed class ItemInterchange : AuditableEntity
{
    public ItemInterchange(Guid id, Guid itemId, Guid interchangeItemId, string type, int priority, Guid createdBy)
        : base(id)
    {
        ItemId = itemId;
        InterchangeItemId = interchangeItemId;
        Type = type;
        Priority = priority;
        IsActive = true;
        CreatedBy = createdBy;
    }

    private ItemInterchange()
        : base(Guid.NewGuid())
    {
    }

    public Guid ItemId { get; private set; }

    public Guid InterchangeItemId { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public int Priority { get; private set; } = 1;

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; }

    public Guid CreatedBy { get; private set; }
}

public sealed class ItemReorderSetting : AuditableEntity
{
    public ItemReorderSetting(Guid id, Guid itemId, Guid warehouseId, decimal reorderPoint, decimal reorderQty, decimal? maxStock)
        : base(id)
    {
        ItemId = itemId;
        WarehouseId = warehouseId;
        ReorderPoint = reorderPoint;
        ReorderQty = reorderQty;
        MaxStock = maxStock;
        IsActive = true;
    }

    private ItemReorderSetting()
        : base(Guid.NewGuid())
    {
    }

    public Guid ItemId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public decimal ReorderPoint { get; private set; }

    public decimal ReorderQty { get; private set; }

    public decimal? MaxStock { get; private set; }

    public bool IsActive { get; private set; }
}


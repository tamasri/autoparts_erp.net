namespace AutoPartsERP.Domain.Operational;

public sealed class Sku : AuditableEntity
{
    private Sku(
        Guid id,
        string code,
        string name,
        string nameAr,
        Guid categoryId,
        decimal sellingPriceSyp,
        decimal sellingPriceUsd,
        decimal minSellingPriceSyp,
        decimal minSellingPriceUsd,
        bool isBatchTracked,
        bool hasWarranty,
        int warrantyMonths,
        Guid createdBy)
        : base(id)
    {
        Code = code;
        Name = name;
        NameAr = nameAr;
        CategoryId = categoryId;
        SellingPriceSyp = sellingPriceSyp;
        SellingPriceUsd = sellingPriceUsd;
        MinSellingPriceSyp = minSellingPriceSyp;
        MinSellingPriceUsd = minSellingPriceUsd;
        IsBatchTracked = isBatchTracked;
        HasWarranty = hasWarranty;
        WarrantyMonths = warrantyMonths;
        IsActive = true;
        CreatedBy = createdBy;
        AttributesJson = "{}";
        Tags = Array.Empty<string>();
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public Guid CategoryId { get; private set; }

    public string? Barcode { get; private set; }

    public string UnitOfMeasure { get; private set; } = "PIECE";

    public decimal CostPriceSyp { get; private set; }

    public decimal CostPriceUsd { get; private set; }

    public decimal SellingPriceSyp { get; private set; }

    public decimal SellingPriceUsd { get; private set; }

    public decimal MinSellingPriceSyp { get; private set; }

    public decimal MinSellingPriceUsd { get; private set; }

    public bool IsBatchTracked { get; private set; }

    public bool HasWarranty { get; private set; }

    public int WarrantyMonths { get; private set; }

    public decimal ReorderLevel { get; private set; }

    public bool IsActive { get; private set; }

    public string? Notes { get; private set; }

    public string AttributesJson { get; private set; }

    public string[] Tags { get; private set; }

    public Guid CreatedBy { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static Result<Sku> Create(
        string code,
        string name,
        string nameAr,
        Guid categoryId,
        decimal sellingPriceSyp,
        decimal sellingPriceUsd,
        decimal minSellingPriceSyp,
        decimal minSellingPriceUsd,
        bool isBatchTracked,
        bool hasWarranty,
        int warrantyMonths,
        Guid createdBy)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(nameAr))
        {
            return Result<Sku>.Failure(new Error("Sku.Required", "Code, Arabic name, and name are required."));
        }

        if (sellingPriceSyp < minSellingPriceSyp || sellingPriceUsd < minSellingPriceUsd)
        {
            return Result<Sku>.Failure(new Error("Sku.PriceBelowMinimum", "Selling price must be greater than or equal to minimum selling price."));
        }

        if (hasWarranty && warrantyMonths <= 0)
        {
            return Result<Sku>.Failure(new Error("Sku.WarrantyMonthsRequired", "Warranty months must be greater than zero when warranty is enabled."));
        }

        return Result<Sku>.Success(new Sku(
            Guid.NewGuid(),
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            nameAr.Trim(),
            categoryId,
            sellingPriceSyp,
            sellingPriceUsd,
            minSellingPriceSyp,
            minSellingPriceUsd,
            isBatchTracked,
            hasWarranty,
            warrantyMonths,
            createdBy));
    }

    public Result UpdatePrices(
        decimal sellingPriceSyp,
        decimal sellingPriceUsd,
        decimal minSellingPriceSyp,
        decimal minSellingPriceUsd,
        Guid by)
    {
        if (sellingPriceSyp < minSellingPriceSyp || sellingPriceUsd < minSellingPriceUsd)
        {
            return Result.Failure(new Error("Sku.PriceBelowMinimum", "Selling price must be greater than or equal to minimum selling price."));
        }

        SellingPriceSyp = sellingPriceSyp;
        SellingPriceUsd = sellingPriceUsd;
        MinSellingPriceSyp = minSellingPriceSyp;
        MinSellingPriceUsd = minSellingPriceUsd;
        UpdatedBy = by;
        Touch();

        return Result.Success();
    }

    public bool IsPriceBelow(decimal priceSyp) => priceSyp < MinSellingPriceSyp;
}

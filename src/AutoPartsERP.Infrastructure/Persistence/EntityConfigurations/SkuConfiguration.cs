namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class SkuConfiguration : IEntityTypeConfiguration<Sku>
{
    public void Configure(EntityTypeBuilder<Sku> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(300).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Barcode).HasMaxLength(100);
        builder.Property(x => x.UnitOfMeasure).HasMaxLength(50).HasDefaultValue("PIECE");
        builder.Property(x => x.CostPriceSyp).HasColumnType("numeric(18,4)");
        builder.Property(x => x.CostPriceUsd).HasColumnType("numeric(18,4)");
        builder.Property(x => x.SellingPriceSyp).HasColumnType("numeric(18,4)");
        builder.Property(x => x.SellingPriceUsd).HasColumnType("numeric(18,4)");
        builder.Property(x => x.MinSellingPriceSyp).HasColumnType("numeric(18,4)");
        builder.Property(x => x.MinSellingPriceUsd).HasColumnType("numeric(18,4)");
        builder.Property(x => x.ReorderLevel).HasColumnType("numeric(18,4)");
        builder.Property(x => x.AttributesJson).HasColumnType("jsonb");
        builder.Property(x => x.Tags).HasColumnType("text[]");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.CategoryId);
        builder.HasIndex(x => x.Barcode).IsUnique();
    }
}

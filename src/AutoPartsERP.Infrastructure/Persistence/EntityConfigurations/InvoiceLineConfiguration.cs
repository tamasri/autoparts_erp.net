namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.LineNumber).IsRequired();
        builder.Property(x => x.Quantity).HasColumnType("numeric(18,4)");
        builder.Property(x => x.UnitPriceSyp).HasColumnType("numeric(18,4)");
        builder.Property(x => x.UnitPriceUsd).HasColumnType("numeric(18,4)");
        builder.Property(x => x.DiscountPct).HasColumnType("numeric(5,2)");
        builder.Property(x => x.CostPriceSyp).HasColumnType("numeric(18,4)");
        builder.Property(x => x.CostPriceUsd).HasColumnType("numeric(18,4)");
        builder.Property(x => x.FxRateUsed).HasColumnType("numeric(18,4)");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.InvoiceId, x.LineNumber }).IsUnique();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_invoice_lines_quantity_positive", "quantity > 0");
            table.HasCheckConstraint("ck_invoice_lines_discount_pct", "discount_pct >= 0 and discount_pct <= 100");
            table.HasCheckConstraint("ck_invoice_lines_unit_price_syp_non_negative", "unit_price_syp >= 0");
        });
    }
}

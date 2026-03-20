namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class InventoryStockConfiguration : IEntityTypeConfiguration<InventoryStock>
{
    public void Configure(EntityTypeBuilder<InventoryStock> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.QuantityOnHand).HasColumnType("numeric(18,4)");
        builder.Property(x => x.QuantityReserved).HasColumnType("numeric(18,4)");
        builder.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.SkuId, x.LocationId }).IsUnique();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_inventory_stock_quantity_on_hand_non_negative", "quantity_on_hand >= 0");
            table.HasCheckConstraint("ck_inventory_stock_quantity_reserved_non_negative", "quantity_reserved >= 0");
            table.HasCheckConstraint("ck_inventory_stock_reserved_lte_on_hand", "quantity_reserved <= quantity_on_hand");
        });
    }
}

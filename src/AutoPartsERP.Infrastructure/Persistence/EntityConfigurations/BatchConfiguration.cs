namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BatchNumber).HasMaxLength(100).IsRequired();
        builder.Property(x => x.QuantityInitial).HasColumnType("numeric(18,4)");
        builder.Property(x => x.QuantityCurrent).HasColumnType("numeric(18,4)");
        builder.Property(x => x.CostPriceSyp).HasColumnType("numeric(18,4)");
        builder.Property(x => x.CostPriceUsd).HasColumnType("numeric(18,4)");
        builder.Property(x => x.ReceivedDate).HasColumnType("date");
        builder.Property(x => x.ExpiryDate).HasColumnType("date");
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.BatchNumber).IsUnique();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_batches_quantity_current_non_negative", "quantity_current >= 0");
        });
    }
}

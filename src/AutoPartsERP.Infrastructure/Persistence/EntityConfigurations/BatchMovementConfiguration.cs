namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class BatchMovementConfiguration : IEntityTypeConfiguration<BatchMovement>
{
    public void Configure(EntityTypeBuilder<BatchMovement> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MovementType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Quantity).HasColumnType("numeric(18,4)");
        builder.Property(x => x.Direction).HasMaxLength(5).IsRequired();
        builder.Property(x => x.UnitCostSyp).HasColumnType("numeric(18,4)");
        builder.Property(x => x.UnitCostUsd).HasColumnType("numeric(18,4)");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_batch_movements_direction", "direction in ('IN','OUT')");
        });
    }
}

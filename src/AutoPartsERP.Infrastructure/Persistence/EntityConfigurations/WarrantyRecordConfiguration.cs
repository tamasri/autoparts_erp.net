namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class WarrantyRecordConfiguration : IEntityTypeConfiguration<WarrantyRecord>
{
    public void Configure(EntityTypeBuilder<WarrantyRecord> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.WarrantyNumber).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SaleDate).HasColumnType("date");
        builder.Property(x => x.ExpiryDate).HasColumnType("date");
        builder.Property(x => x.ClaimDate).HasColumnType("date");
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ProcessedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.WarrantyNumber).IsUnique();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_warranty_records_status", "status in ('Active','Claimed','Expired','Rejected','Voided')");
        });
    }
}

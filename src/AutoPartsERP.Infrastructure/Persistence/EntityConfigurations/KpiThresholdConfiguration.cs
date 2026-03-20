namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class KpiThresholdConfiguration : IEntityTypeConfiguration<KpiThreshold>
{
    public void Configure(EntityTypeBuilder<KpiThreshold> builder)
    {
        builder.ToTable("kpi_thresholds");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.WarningValue).HasColumnType("numeric(18,4)");
        builder.Property(x => x.CriticalValue).HasColumnType("numeric(18,4)");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");

        builder.HasOne<KpiDefinition>()
            .WithMany()
            .HasForeignKey(x => x.KpiDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

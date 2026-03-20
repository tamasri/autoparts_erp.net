namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class PeriodLockConfiguration : IEntityTypeConfiguration<PeriodLock>
{
    public void Configure(EntityTypeBuilder<PeriodLock> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PeriodKey).HasMaxLength(7).IsRequired();
        builder.Property(x => x.ModuleCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.LockedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.PeriodKey, x.ModuleCode }).IsUnique();
    }
}
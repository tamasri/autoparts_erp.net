namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AllocatedSyp).HasColumnType("numeric(18,4)");
        builder.Property(x => x.AllocatedUsd).HasColumnType("numeric(18,4)");
        builder.Property(x => x.AllocationDate).HasColumnType("date");
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.PaymentId, x.InvoiceId }).IsUnique();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_payment_allocations_non_negative", "allocated_syp >= 0 and allocated_usd >= 0");
            table.HasCheckConstraint("ck_payment_allocations_positive_either", "allocated_syp > 0 or allocated_usd > 0");
        });
    }
}

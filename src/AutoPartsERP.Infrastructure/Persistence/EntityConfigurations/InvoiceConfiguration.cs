namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.InvoiceNumber).HasMaxLength(100);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.InvoiceDate).HasColumnType("date");
        builder.Property(x => x.DueDate).HasColumnType("date");
        builder.Property(x => x.SubtotalSyp).HasColumnType("numeric(18,4)");
        builder.Property(x => x.SubtotalUsd).HasColumnType("numeric(18,4)");
        builder.Property(x => x.DiscountAmountSyp).HasColumnType("numeric(18,4)");
        builder.Property(x => x.DiscountAmountUsd).HasColumnType("numeric(18,4)");
        builder.Property(x => x.DeliveryFeeSyp).HasColumnType("numeric(18,4)");
        builder.Property(x => x.DeliveryFeeUsd).HasColumnType("numeric(18,4)");
        builder.Property(x => x.TaxAmountSyp).HasColumnType("numeric(18,4)");
        builder.Property(x => x.TaxAmountUsd).HasColumnType("numeric(18,4)");
        builder.Property(x => x.TotalSyp).HasColumnType("numeric(18,4)");
        builder.Property(x => x.TotalUsd).HasColumnType("numeric(18,4)");
        builder.Property(x => x.PaidSyp).HasColumnType("numeric(18,4)");
        builder.Property(x => x.PaidUsd).HasColumnType("numeric(18,4)");
        builder.Property(x => x.FxRateSnapshot).HasColumnType("numeric(18,4)");
        builder.Property(x => x.PostedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.VoidedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.InvoiceNumber).IsUnique();
        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

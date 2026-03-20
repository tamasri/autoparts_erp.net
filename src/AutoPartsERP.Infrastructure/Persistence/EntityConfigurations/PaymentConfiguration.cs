namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PaymentNumber).HasMaxLength(100);
        builder.Property(x => x.PaymentType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.PaymentDate).HasColumnType("date");
        builder.Property(x => x.AmountSyp).HasColumnType("numeric(18,4)");
        builder.Property(x => x.AmountUsd).HasColumnType("numeric(18,4)");
        builder.Property(x => x.AllocatedSyp).HasColumnType("numeric(18,4)");
        builder.Property(x => x.AllocatedUsd).HasColumnType("numeric(18,4)");
        builder.Property(x => x.ReversedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.PaymentNumber).IsUnique();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_payments_non_negative_amounts", "amount_syp >= 0 and amount_usd >= 0");
            table.HasCheckConstraint("ck_payments_positive_either_currency", "amount_syp > 0 or amount_usd > 0");
        });
    }
}

namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class FxRateConfiguration : IEntityTypeConfiguration<FxRate>
{
    public void Configure(EntityTypeBuilder<FxRate> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RateDate).HasColumnType("date");
        builder.Property(x => x.CurrencyFrom).HasMaxLength(10).IsRequired();
        builder.Property(x => x.CurrencyTo).HasMaxLength(10).IsRequired();
        builder.Property(x => x.BuyRate).HasColumnType("numeric(18,4)");
        builder.Property(x => x.SellRate).HasColumnType("numeric(18,4)");
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.RateDate, x.CurrencyFrom, x.CurrencyTo }).IsUnique();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_fx_rates_buy_positive", "buy_rate > 0");
            table.HasCheckConstraint("ck_fx_rates_sell_positive", "sell_rate > 0");
            table.HasCheckConstraint("ck_fx_rates_spread", "buy_rate >= sell_rate");
        });
    }
}

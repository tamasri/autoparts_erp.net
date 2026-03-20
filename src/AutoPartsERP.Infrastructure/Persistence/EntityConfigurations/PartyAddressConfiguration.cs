namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class PartyAddressConfiguration : IEntityTypeConfiguration<PartyAddress>
{
    public void Configure(EntityTypeBuilder<PartyAddress> builder)
    {
        builder.ToTable("party_addresses");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Line1).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Line2).HasMaxLength(300);
        builder.Property(x => x.City).HasMaxLength(100);
        builder.Property(x => x.Region).HasMaxLength(100);
        builder.Property(x => x.Country).HasMaxLength(5).HasDefaultValue("SY").IsRequired();
        builder.Property(x => x.IsDefault).HasDefaultValue(false);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
    }
}

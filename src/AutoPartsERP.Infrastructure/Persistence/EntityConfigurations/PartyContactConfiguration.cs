namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class PartyContactConfiguration : IEntityTypeConfiguration<PartyContact>
{
    public void Configure(EntityTypeBuilder<PartyContact> builder)
    {
        builder.ToTable("party_contacts");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Value).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(100);
        builder.Property(x => x.IsPrimary).HasDefaultValue(false);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
    }
}

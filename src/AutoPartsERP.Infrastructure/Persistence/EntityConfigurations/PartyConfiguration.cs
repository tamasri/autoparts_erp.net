namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.ToTable("parties");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DisplayNameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TaxNumber).HasMaxLength(100);
        builder.Property(x => x.Website).HasMaxLength(500);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");

        builder.HasIndex(x => x.Code).IsUnique();

        builder.HasMany(x => x.TypeAssignments)
            .WithOne()
            .HasForeignKey(x => x.PartyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Contacts)
            .WithOne()
            .HasForeignKey(x => x.PartyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Addresses)
            .WithOne()
            .HasForeignKey(x => x.PartyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.NotesEntries)
            .WithOne()
            .HasForeignKey(x => x.PartyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

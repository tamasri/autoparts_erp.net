namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class PartyNoteConfiguration : IEntityTypeConfiguration<PartyNote>
{
    public void Configure(EntityTypeBuilder<PartyNote> builder)
    {
        builder.ToTable("party_notes");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.IsPinned).HasDefaultValue(false);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");
    }
}

namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class PartyTypeAssignmentConfiguration : IEntityTypeConfiguration<PartyTypeAssignment>
{
    public void Configure(EntityTypeBuilder<PartyTypeAssignment> builder)
    {
        builder.ToTable("party_type_assignments");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.TypeCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(false);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("timestamp with time zone");

        builder.HasIndex(x => new { x.PartyId, x.TypeCode }).IsUnique();
    }
}

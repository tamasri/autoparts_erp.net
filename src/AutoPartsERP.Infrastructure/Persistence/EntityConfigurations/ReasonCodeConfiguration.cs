namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class ReasonCodeConfiguration : IEntityTypeConfiguration<ReasonCode>
{
    public void Configure(EntityTypeBuilder<ReasonCode> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Category).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
    }
}
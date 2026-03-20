namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Path)
            .HasConversion(
                value => value.Value,
                value => new CategoryPath(value))
            .HasColumnType("ltree")
            .IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.Path).IsUnique();
    }
}

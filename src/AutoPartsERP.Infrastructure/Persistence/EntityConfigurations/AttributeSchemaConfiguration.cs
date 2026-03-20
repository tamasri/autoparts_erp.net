namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class AttributeSchemaConfiguration : IEntityTypeConfiguration<AttributeSchema>
{
    public void Configure(EntityTypeBuilder<AttributeSchema> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(200).IsRequired();
        builder.Property(x => x.LabelAr).HasMaxLength(200);
        builder.Property(x => x.DataType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.OptionsJson).HasColumnType("jsonb");
        builder.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.CategoryId, x.Code }).IsUnique();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_attribute_schemas_data_type", "data_type in ('TEXT','NUMBER','BOOLEAN','SELECT')");
        });
    }
}

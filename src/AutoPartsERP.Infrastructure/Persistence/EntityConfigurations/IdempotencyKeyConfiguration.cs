namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Key).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Scope).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ExpiresAtUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.Key, x.Scope }).IsUnique();
    }
}
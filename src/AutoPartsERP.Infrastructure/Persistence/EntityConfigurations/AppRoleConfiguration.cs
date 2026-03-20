using AutoPartsERP.Infrastructure.Identity;

namespace AutoPartsERP.Infrastructure.Persistence.EntityConfigurations;

public sealed class AppRoleConfiguration : IEntityTypeConfiguration<AppRole>
{
    public void Configure(EntityTypeBuilder<AppRole> builder)
    {
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
    }
}